using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Tools.Exceptions;
using Xunit;

namespace EquipmentInspection.Tests;

public sealed class InspectionContractTests
{
    [Fact]
    public void Incomplete_drafts_have_no_business_validator_and_posting_has_no_effect_handlers()
    {
        var builder = new DefinitionsBuilder();
        new EquipmentDefinitions().Contribute(builder);
        new InspectionRuntimeDefinitions().Contribute(builder);
        var registry = builder.Build();

        Assert.Equal(EquipmentDefinitions.EquipmentCode,
            registry.GetCatalog(EquipmentDefinitions.EquipmentCode).TypeCode);

        var inspection = registry.GetDocument(EquipmentDefinitions.InspectionCode);
        Assert.Empty(inspection.DraftValidatorTypes);
        Assert.Equal([typeof(InspectionPostValidator)], inspection.PostValidatorTypes);
        Assert.Null(inspection.PostingHandlerType);
        Assert.Null(inspection.OperationalRegisterPostingHandlerType);
        Assert.Null(inspection.ReferenceRegisterPostingHandlerType);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("00000000-0000-0000-0000-000000000000", "Checked")]
    [InlineData("01900000-0000-7000-8000-000000000001", null)]
    [InlineData("01900000-0000-7000-8000-000000000001", " ")]
    public async Task Posting_requires_equipment_and_a_result(string? equipment, string? result)
    {
        var reader = new InspectionReader(equipment is null ? null : new InspectionDetails(Guid.Parse(equipment), result));
        var validator = new InspectionPostValidator(reader);

        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            validator.ValidateBeforePostAsync(Document(), CancellationToken.None));
    }

    [Fact]
    public async Task A_complete_inspection_passes_the_public_post_validation_contract()
    {
        var document = Document();
        var reader = new InspectionReader(new InspectionDetails(Guid.CreateVersion7(), "Checked"));
        var validator = new InspectionPostValidator(reader);

        await validator.ValidateBeforePostAsync(document, CancellationToken.None);

        Assert.Equal(EquipmentDefinitions.InspectionCode, validator.TypeCode);
        Assert.Equal(document.Id, reader.RequestedDocumentId);
    }

    [Fact]
    public async Task Cancellation_does_not_read_storage()
    {
        var reader = new InspectionReader(null);
        var validator = new InspectionPostValidator(reader);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            validator.ValidateBeforePostAsync(Document(), cancellation.Token));

        Assert.Null(reader.RequestedDocumentId);
    }

    [Fact]
    public void Workflow_registration_preserves_post_validation_without_requiring_register_metadata()
    {
        var builder = new DefinitionsBuilder();
        new EquipmentDefinitions().Contribute(builder);
        new InspectionRuntimeDefinitions().Contribute(builder);
        new InspectionWorkflowDefinitions().Contribute(builder);

        var inspection = builder.Build().GetDocument(EquipmentDefinitions.InspectionCode);

        Assert.Equal(typeof(InspectionWorkflowPostingHandler), inspection.ReferenceRegisterPostingHandlerType);
        Assert.Equal([typeof(InspectionPostValidator)], inspection.PostValidatorTypes);
        Assert.Null(inspection.PostingHandlerType);
        Assert.Null(inspection.OperationalRegisterPostingHandlerType);
    }

    [Theory]
    [InlineData(ReferenceRegisterWriteOperation.Post)]
    [InlineData(ReferenceRegisterWriteOperation.Unpost)]
    [InlineData(ReferenceRegisterWriteOperation.Repost)]
    public async Task Workflow_handler_does_not_create_register_records(ReferenceRegisterWriteOperation operation)
    {
        var handler = new InspectionWorkflowPostingHandler();
        var records = new RecordingRecordsBuilder();

        await handler.BuildRecordsAsync(Document(), operation, records, CancellationToken.None);

        Assert.Equal(EquipmentDefinitions.InspectionCode, handler.TypeCode);
        Assert.Empty(records.RegisterCodes);
    }

    [Fact]
    public async Task Workflow_handler_honors_cancellation()
    {
        var handler = new InspectionWorkflowPostingHandler();
        var records = new RecordingRecordsBuilder();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.BuildRecordsAsync(
            Document(), ReferenceRegisterWriteOperation.Post, records, cancellation.Token));

        Assert.Empty(records.RegisterCodes);
    }

    private static DocumentRecord Document() => new()
    {
        Id = Guid.CreateVersion7(),
        TypeCode = EquipmentDefinitions.InspectionCode,
        DateUtc = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc),
        Status = DocumentStatus.Draft
    };

    private sealed class InspectionReader(InspectionDetails? details) : IInspectionReader
    {
        public Guid? RequestedDocumentId { get; private set; }

        public Task<InspectionDetails?> ReadAsync(Guid documentId, CancellationToken ct)
        {
            RequestedDocumentId = documentId;
            return Task.FromResult(details);
        }
    }

    private sealed class RecordingRecordsBuilder : IReferenceRegisterRecordsBuilder
    {
        public List<string> RegisterCodes { get; } = [];

        public void Add(string registerCode, ReferenceRegisterRecordWrite record)
        {
            RegisterCodes.Add(registerCode);
        }
    }
}
