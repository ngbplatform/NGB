using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Core.Documents.Actions;
using NGB.Core.Security;
using NGB.Definitions.Documents.Actions;
using NGB.Runtime.Documents;
using Xunit;
using ContractStatus = NGB.Contracts.Metadata.DocumentStatus;

namespace EquipmentInspection.Tests;

public sealed class InspectionDeferralTests
{
    [Theory]
    [InlineData(true, true, true, false, false, true)]
    [InlineData(true, true, false, true, true, true)]
    [InlineData(true, true, false, true, false, false)]
    [InlineData(true, true, false, false, true, false)]
    [InlineData(true, false, true, true, true, false)]
    [InlineData(false, true, true, true, true, false)]
    public async Task Authorization_requires_active_identity_view_and_draft_edit(
        bool authenticated, bool active, bool administrator, bool view, bool edit, bool allowed)
    {
        var permissions = new HashSet<NgbPermissionKey>();
        if (view)
            permissions.Add(new("document", EquipmentDefinitions.InspectionCode, NgbPermissionActions.View));
        if (edit)
            permissions.Add(new("document", EquipmentDefinitions.InspectionCode, NgbPermissionActions.EditDraft));
        var context = Context() with
        {
            Security = new DocumentActionSecurityContext(Guid.CreateVersion7(), authenticated, active, administrator, permissions)
        };

        var result = await new InspectionDeferralAuthorization().EvaluateAsync(context, CancellationToken.None);
        Assert.Equal(allowed, result.IsAuthorized);
    }

    [Theory]
    [InlineData(DocumentStatus.Draft, true, false, null)]
    [InlineData(DocumentStatus.Posted, true, false, "inspection.deferral.not-draft")]
    [InlineData(DocumentStatus.Draft, false, false, "inspection.deferral.equipment-required")]
    [InlineData(DocumentStatus.Draft, null, false, "inspection.deferral.equipment-required")]
    [InlineData(DocumentStatus.Draft, true, true, "inspection.deferral.date-limit")]
    public async Task Availability_is_pure_and_reports_the_specific_business_prerequisite(
        DocumentStatus status, bool? selected, bool lastDay, string? reason)
    {
        var context = Context(status, lastDay);
        if (selected.HasValue)
            context = context with { Facts = new Dictionary<string, object?> { [InspectionDeferralDefinitions.EquipmentSelectedFact] = selected.Value } };
        var result = await new InspectionDeferralAvailability().EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(reason is null, result.IsAllowed);
        if (reason is null)
            Assert.Empty(result.DisabledReasons);
        else
            Assert.Equal(reason, Assert.Single(result.DisabledReasons).Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData(1)]
    public async Task Availability_rejects_missing_or_mistyped_fact_values(object? selected)
    {
        var context = Context() with
        {
            Facts = new Dictionary<string, object?>
            {
                [InspectionDeferralDefinitions.EquipmentSelectedFact] = selected
            }
        };

        var result = await new InspectionDeferralAvailability().EvaluateAsync(context, CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Equal("inspection.deferral.equipment-required", Assert.Single(result.DisabledReasons).Code);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("00000000-0000-0000-0000-000000000000", false)]
    [InlineData("01900000-0000-7000-8000-000000000001", true)]
    public async Task Context_reads_one_authorized_document_and_returns_only_a_boolean_fact(string? equipment, bool selected)
    {
        var reader = new Reader(equipment is null ? null : new InspectionDetails(Guid.Parse(equipment), "Private result"));
        var enricher = new InspectionActionContext(reader);
        var context = Context();
        var request = new DocumentActionContextRequest(context.Document, context.DocumentDto, context.Security);

        var facts = await enricher.LoadFactsAsync(request, CancellationToken.None);

        Assert.Equal(EquipmentDefinitions.InspectionCode, enricher.DocumentTypeCode);
        Assert.Equal(context.Document.Id, Assert.Single(reader.Reads));
        var fact = Assert.Single(facts);
        Assert.Equal(InspectionDeferralDefinitions.EquipmentSelectedFact, fact.Key);
        Assert.Equal(selected, Assert.IsType<bool>(fact.Value));
    }

    [Fact]
    public async Task Context_does_not_read_storage_without_source_view_permission()
    {
        var reader = new Reader(null);
        var context = Context();
        var request = new DocumentActionContextRequest(context.Document, context.DocumentDto,
            new DocumentActionSecurityContext(Guid.CreateVersion7(), true, true, false, new HashSet<NgbPermissionKey>()));

        Assert.Empty(await new InspectionActionContext(reader).LoadFactsAsync(request, CancellationToken.None));
        Assert.Empty(reader.Reads);
    }

    [Fact]
    public async Task Handler_advances_one_day_and_joins_the_dispatcher_transaction()
    {
        var drafts = new Drafts();
        var context = Context();
        var handler = new InspectionDeferralHandler(drafts);
        var result = await handler.ExecuteAsync(new DocumentActionHandlerContext(Guid.CreateVersion7(),
            new DocumentActionCode(InspectionDeferralDefinitions.ActionCode), context.Document, context.DocumentDto,
            null, "Equipment unavailable", context.Security.UserId), CancellationToken.None);

        Assert.Null(result.CreatedDocumentId);
        Assert.Equal(context.Document.Id, drafts.DocumentId);
        Assert.Equal(context.Document.DateUtc.AddDays(1), drafts.DateUtc);
        Assert.Null(drafts.Number);
        Assert.False(drafts.OwnsTransaction);
    }

    [Fact]
    public async Task Cancellation_stops_facts_rules_and_mutation_before_work_begins()
    {
        var context = Context();
        var reader = new Reader(null);
        var drafts = new Drafts();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var ct = cancellation.Token;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new InspectionActionContext(reader)
            .LoadFactsAsync(new DocumentActionContextRequest(context.Document, context.DocumentDto, context.Security), ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new InspectionDeferralAvailability().EvaluateAsync(context, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new InspectionDeferralAuthorization().EvaluateAsync(context, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new InspectionDeferralHandler(drafts)
            .ExecuteAsync(new DocumentActionHandlerContext(Guid.CreateVersion7(), new DocumentActionCode(InspectionDeferralDefinitions.ActionCode),
                context.Document, context.DocumentDto, null, "Equipment unavailable", context.Security.UserId), ct));
        Assert.Empty(reader.Reads);
        Assert.Null(drafts.DocumentId);
    }

    [Fact]
    public void Definition_requires_reason_and_binds_all_public_extensions()
    {
        var builder = new DocumentActionDefinitionsBuilder();
        new InspectionDeferralDefinitions().Contribute(builder);
        var definition = Assert.Single(builder.Build());

        Assert.Equal(EquipmentDefinitions.InspectionCode, definition.DocumentTypeCode);
        Assert.Equal(InspectionDeferralDefinitions.ActionCode, definition.Metadata.Code.Value);
        Assert.Equal(DocumentActionConfirmationMode.RequireReason, definition.Metadata.Confirmation!.Mode);
        Assert.Equal(typeof(InspectionDeferralHandler), definition.HandlerType);
        Assert.Equal(typeof(InspectionDeferralAvailability), definition.AvailabilityEvaluatorType);
        Assert.Equal(typeof(InspectionDeferralAuthorization), definition.AuthorizationEvaluatorType);
    }

    private static DocumentActionEvaluationContext Context(DocumentStatus status = DocumentStatus.Draft, bool lastDay = false)
    {
        var record = new DocumentRecord
        {
            Id = Guid.CreateVersion7(),
            TypeCode = EquipmentDefinitions.InspectionCode,
            DateUtc = lastDay ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc) : new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc),
            Status = status
        };
        var dto = new DocumentDto(record.Id, "Inspection", new RecordPayload(),
            status == DocumentStatus.Posted ? ContractStatus.Posted : ContractStatus.Draft, false);
        return new DocumentActionEvaluationContext(record, dto,
            new DocumentActionSecurityContext(Guid.CreateVersion7(), true, true, true, new HashSet<NgbPermissionKey>()),
            new Dictionary<string, object?>());
    }

    private sealed class Reader(InspectionDetails? details) : IInspectionReader
    {
        public List<Guid> Reads { get; } = [];

        public Task<InspectionDetails?> ReadAsync(Guid documentId, CancellationToken ct)
        {
            Reads.Add(documentId);
            return Task.FromResult(details);
        }
    }

    private sealed class Drafts : IDocumentDraftService
    {
        public Guid? DocumentId { get; private set; }
        public DateTime? DateUtc { get; private set; }
        public string? Number { get; private set; }
        public bool OwnsTransaction { get; private set; }

        public Task<bool> UpdateDraftAsync(Guid documentId, string? number, DateTime? dateUtc,
            bool manageTransaction = true, CancellationToken ct = default)
        {
            DocumentId = documentId;
            Number = number;
            DateUtc = dateUtc;
            OwnsTransaction = manageTransaction;
            return Task.FromResult(true);
        }

        public Task<Guid> CreateDraftAsync(string typeCode, string? number, DateTime dateUtc,
            bool manageTransaction = true, bool suppressAudit = false, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteDraftAsync(Guid documentId, bool manageTransaction = true, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
