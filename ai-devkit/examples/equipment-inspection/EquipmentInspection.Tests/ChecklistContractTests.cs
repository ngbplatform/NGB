using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using NGB.Contracts.Common;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Approval;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Documents.Policies;
using NGB.Tools.Exceptions;
using Xunit;

namespace EquipmentInspection.Tests;

public sealed class ChecklistContractTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Common_draft_validation_allows_current_and_past_observation_dates(int dayOffset)
    {
        var clock = new FixedClock();
        var validator = new ChecklistDraftValidator(clock);
        await validator.ValidateCreateDraftAsync(Document(clock.GetUtcNow().UtcDateTime.AddDays(dayOffset)), CancellationToken.None);
        Assert.Equal(ChecklistDefinitions.DocumentCode, validator.TypeCode);
    }

    [Fact]
    public async Task Common_draft_validation_rejects_future_dates_and_honors_cancellation()
    {
        var clock = new FixedClock();
        var validator = new ChecklistDraftValidator(clock);
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => validator.ValidateCreateDraftAsync(
            Document(clock.GetUtcNow().UtcDateTime.AddDays(1)), CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            validator.ValidateCreateDraftAsync(null!, cancellation.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Approval_requires_an_explicit_review_even_when_all_checks_are_complete(bool? reviewed)
    {
        var reader = new ChecklistReader(new ChecklistSummary(reviewed, 2, 0));
        var policy = new ChecklistApprovalPolicy(reader);
        var validator = new ChecklistPostValidator(reader, new ApprovalResolver(policy));
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => validator.ValidateBeforePostAsync(Document(), CancellationToken.None));
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => policy.EnsureCanPostAsync(Document()));
        Assert.Equal(ChecklistDefinitions.DocumentCode, policy.TypeCode);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    public async Task Review_does_not_replace_post_time_completeness(long count, long incomplete)
    {
        var reader = new ChecklistReader(new ChecklistSummary(true, count, incomplete));
        var policy = new ChecklistApprovalPolicy(reader);
        var validator = new ChecklistPostValidator(reader, new ApprovalResolver(policy));
        await policy.EnsureCanPostAsync(Document());
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => validator.ValidateBeforePostAsync(Document(), CancellationToken.None));
    }

    [Fact]
    public async Task Missing_storage_is_rejected_and_cancellation_does_not_read_it()
    {
        var reader = new ChecklistReader(null);
        var policy = new ChecklistApprovalPolicy(reader);
        var validator = new ChecklistPostValidator(reader, new ApprovalResolver(policy));
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => policy.EnsureCanPostAsync(Document()));
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => validator.ValidateBeforePostAsync(Document(), CancellationToken.None));
        Assert.Equal(2, reader.Reads);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => policy.EnsureCanPostAsync(Document(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validator.ValidateBeforePostAsync(Document(), cancellation.Token));
        Assert.Equal(2, reader.Reads);
    }

    [Fact]
    public async Task Completed_checks_still_require_a_resolvable_approval_binding()
    {
        var reader = new ChecklistReader(new ChecklistSummary(true, 1, 0));
        var validator = new ChecklistPostValidator(reader, new ApprovalResolver(null));
        await Assert.ThrowsAsync<NgbConfigurationViolationException>(() => validator.ValidateBeforePostAsync(Document(), CancellationToken.None));
        var resolved = new ChecklistPostValidator(reader, new ApprovalResolver(new ChecklistApprovalPolicy(reader)));
        await resolved.ValidateBeforePostAsync(Document(), CancellationToken.None);
        Assert.Equal(ChecklistDefinitions.DocumentCode, resolved.TypeCode);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public async Task Draft_payload_validation_rejects_nonpositive_or_duplicate_line_numbers_on_both_paths(int first, int second)
    {
        var validator = new ChecklistPayloadValidator();
        var parts = Parts(first, second);
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), parts, CancellationToken.None));
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            validator.ValidateUpdateDraftPayloadAsync(Guid.CreateVersion7(), new RecordPayload(), parts, CancellationToken.None));
    }

    [Fact]
    public async Task Draft_payload_validation_allows_incomplete_checks_and_cancellation_stops_before_parsing()
    {
        var validator = new ChecklistPayloadValidator();
        await validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Parts(1, 2), CancellationToken.None);
        await validator.ValidateUpdateDraftPayloadAsync(Guid.CreateVersion7(), new RecordPayload(), Parts(), CancellationToken.None);
        await validator.ValidateCreateDraftPayloadAsync(new RecordPayload(),
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(), CancellationToken.None);
        Assert.Equal(ChecklistDefinitions.DocumentCode, validator.TypeCode);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), null!, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            validator.ValidateUpdateDraftPayloadAsync(Guid.CreateVersion7(), new RecordPayload(), null!, cancellation.Token));
    }

    [Theory]
    [InlineData(ReferenceRegisterWriteOperation.Post)]
    [InlineData(ReferenceRegisterWriteOperation.Repost)]
    [InlineData(ReferenceRegisterWriteOperation.Unpost)]
    public async Task Checklist_lifecycle_has_no_register_effects_and_respects_cancellation(ReferenceRegisterWriteOperation operation)
    {
        var handler = new ChecklistWorkflowPostingHandler();
        var records = new RecordingRecordsBuilder();
        await handler.BuildRecordsAsync(Document(), operation, records, CancellationToken.None);
        Assert.Equal(ChecklistDefinitions.DocumentCode, handler.TypeCode);
        Assert.Empty(records.Codes);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.BuildRecordsAsync(Document(), operation, records, cancellation.Token));
        Assert.Empty(records.Codes);
    }

    private static DocumentRecord Document(DateTime? date = null) => new()
    {
        Id = Guid.CreateVersion7(),
        TypeCode = ChecklistDefinitions.DocumentCode,
        DateUtc = date ?? new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
        Status = DocumentStatus.Draft
    };

    private static IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> Parts(params int[] lines)
    {
        return new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>
        {
            [ChecklistDefinitions.ChecksPartCode] = lines.Select(line =>
                (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["line_no"] = line }).ToArray()
        };
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class ApprovalResolver(IDocumentApprovalPolicy? policy) : IDocumentApprovalPolicyResolver
    {
        public IDocumentApprovalPolicy? Resolve(string typeCode)
        {
            Assert.Equal(ChecklistDefinitions.DocumentCode, typeCode);
            return policy;
        }
    }

    private sealed class ChecklistReader(ChecklistSummary? summary) : IChecklistReader
    {
        public int Reads { get; private set; }

        public Task<ChecklistSummary?> ReadAsync(Guid documentId, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(summary);
        }
    }

    private sealed class RecordingRecordsBuilder : IReferenceRegisterRecordsBuilder
    {
        public List<string> Codes { get; } = [];

        public void Add(string registerCode, ReferenceRegisterRecordWrite record)
        {
            Codes.Add(registerCode);
        }
    }
}
