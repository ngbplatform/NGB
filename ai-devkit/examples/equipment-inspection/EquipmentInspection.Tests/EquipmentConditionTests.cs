using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Dimensions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace EquipmentInspection.Tests;

public sealed class EquipmentConditionTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("00000000-0000-0000-0000-000000000000", "Ready")]
    [InlineData("01900000-0000-7000-8000-000000000001", null)]
    [InlineData("01900000-0000-7000-8000-000000000001", " ")]
    public async Task Incomplete_assessment_is_rejected_before_records_are_built(string? equipment, string? condition)
    {
        var reader = new ConditionReader(equipment is null ? null : new EquipmentConditionDetails(Guid.Parse(equipment), condition));
        var validator = new EquipmentConditionPostValidator(reader);

        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => validator.ValidateBeforePostAsync(Document(), CancellationToken.None));
    }

    [Fact]
    public async Task Existing_draft_without_equipment_is_rejected()
    {
        var validator = new EquipmentConditionPostValidator(new ConditionReader(new EquipmentConditionDetails(null, "Ready")));

        var error = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            validator.ValidateBeforePostAsync(Document(), CancellationToken.None));

        Assert.Equal("equipment_id", error.ParamName);
    }

    [Theory]
    [InlineData(ReferenceRegisterWriteOperation.Post)]
    [InlineData(ReferenceRegisterWriteOperation.Repost)]
    public async Task Post_and_repost_build_nonperiodic_records_owned_by_the_document(ReferenceRegisterWriteOperation operation)
    {
        var equipment = Guid.CreateVersion7();
        var document = Document();
        var reader = new ConditionReader(new EquipmentConditionDetails(equipment, "Ready for service"));
        var validator = new EquipmentConditionPostValidator(reader);
        var dimensions = new DimensionSets();
        var handler = new EquipmentConditionPostingHandler(validator, dimensions);
        var records = new Records();

        await validator.ValidateBeforePostAsync(document, CancellationToken.None);
        await handler.BuildRecordsAsync(document, operation, records, CancellationToken.None);

        Assert.Equal(EquipmentConditionDefinitions.DocumentCode, validator.TypeCode);
        Assert.Equal(EquipmentConditionDefinitions.DocumentCode, handler.TypeCode);
        Assert.Equal(DeterministicGuid.Create("Dimension|example.equipment"), EquipmentDefinitions.EquipmentDimensionId);
        Assert.Equal(document.Id, reader.DocumentId);
        Assert.Equal(new DimensionValue(EquipmentDefinitions.EquipmentDimensionId, equipment), Assert.Single(dimensions.Bag!));
        var (register, record) = Assert.Single(records.Items);
        Assert.Equal(EquipmentConditionDefinitions.RegisterCode, register);
        Assert.Equal(document.Id, record.RecorderDocumentId);
        Assert.Null(record.PeriodUtc);
        Assert.Equal(dimensions.Id, record.DimensionSetId);
        Assert.Equal("Ready for service", record.Values["condition"]);
        Assert.False(record.IsDeleted);
    }

    [Fact]
    public async Task Unpost_leaves_tombstones_to_the_engine_without_reading_a_changed_draft()
    {
        var reader = new ConditionReader(null);
        var dimensions = new DimensionSets();
        var records = new Records();
        var handler = new EquipmentConditionPostingHandler(new EquipmentConditionPostValidator(reader), dimensions);

        await handler.BuildRecordsAsync(Document(), ReferenceRegisterWriteOperation.Unpost, records, CancellationToken.None);

        Assert.Null(reader.DocumentId);
        Assert.Null(dimensions.Bag);
        Assert.Empty(records.Items);
    }

    [Theory]
    [InlineData(ReferenceRegisterWriteOperation.Post)]
    [InlineData(ReferenceRegisterWriteOperation.Unpost)]
    public async Task Cancellation_does_not_read_or_build_records(ReferenceRegisterWriteOperation operation)
    {
        var reader = new ConditionReader(null);
        var dimensions = new DimensionSets();
        var records = new Records();
        var handler = new EquipmentConditionPostingHandler(new EquipmentConditionPostValidator(reader), dimensions);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.BuildRecordsAsync(Document(), operation, records, cancellation.Token));

        Assert.Null(reader.DocumentId);
        Assert.Null(dimensions.Bag);
        Assert.Empty(records.Items);
    }

    private static DocumentRecord Document() => new()
    {
        Id = Guid.CreateVersion7(),
        TypeCode = EquipmentConditionDefinitions.DocumentCode,
        DateUtc = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc),
        Status = DocumentStatus.Draft
    };

    private sealed class ConditionReader(EquipmentConditionDetails? details) : IEquipmentConditionReader
    {
        public Guid? DocumentId { get; private set; }

        public Task<EquipmentConditionDetails?> ReadAsync(Guid documentId, CancellationToken ct)
        {
            DocumentId = documentId;
            return Task.FromResult(details);
        }
    }

    private sealed class DimensionSets : IDimensionSetService
    {
        public Guid Id { get; } = Guid.CreateVersion7();
        public DimensionBag? Bag { get; private set; }

        public Task<Guid> GetOrCreateIdAsync(DimensionBag bag, CancellationToken ct = default)
        {
            Bag = bag;
            return Task.FromResult(Id);
        }

        public Task<IReadOnlyList<Guid>> GetOrCreateIdsAsync(IReadOnlyList<DimensionBag> bags, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class Records : IReferenceRegisterRecordsBuilder
    {
        public List<(string Register, ReferenceRegisterRecordWrite Record)> Items { get; } = [];

        public void Add(string registerCode, ReferenceRegisterRecordWrite record)
        {
            Items.Add((registerCode, record));
        }
    }
}
