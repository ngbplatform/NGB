using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;

namespace NGB.Runtime.Documents.GeneralJournalEntry;

public sealed record GeneralJournalEntryDraftLineInput(
    GeneralJournalEntryModels.LineSide Side,
    Guid AccountId,
    decimal Amount,
    string? Memo,
    IReadOnlyList<DimensionValue>? Dimensions = null);

public sealed record GeneralJournalEntryDraftSnapshot(
    DocumentRecord Document,
    GeneralJournalEntryHeaderRecord Header,
    IReadOnlyList<GeneralJournalEntryDraftLineSnapshot> Lines);

public sealed record GeneralJournalEntryDraftLineSnapshot(
    int LineNo,
    GeneralJournalEntryModels.LineSide Side,
    Guid AccountId,
    decimal Amount,
    string? Memo,
    Guid DimensionSetId,
    DimensionBag Dimensions);

public sealed record GeneralJournalEntryDraftHeaderUpdate(
    GeneralJournalEntryModels.JournalType? JournalType,
    string? ReasonCode,
    string? Memo,
    string? ExternalReference,
    bool? AutoReverse,
    DateOnly? AutoReverseOnUtc);

public interface IGeneralJournalEntryDocumentService
{
    Task<Guid> CreateDraftAsync(
        DateTime dateUtc,
        string initiatedBy,
        CancellationToken ct = default,
        Guid? createdFromDocumentId = null,
        IReadOnlyList<Guid>? basedOnDocumentIds = null);

    Task<GeneralJournalEntryDraftSnapshot> GetDraftAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Convenience composition that executes as a single atomic transaction:
    /// Create Draft → (optional header/lines) → Submit → Approve → Post.
    /// </summary>
    Task<Guid> CreateAndPostApprovedAsync(
        DateTime dateUtc,
        GeneralJournalEntryDraftHeaderUpdate? header,
        IReadOnlyList<GeneralJournalEntryDraftLineInput>? lines,
        string initiatedBy,
        string submittedBy,
        string approvedBy,
        string postedBy,
        CancellationToken ct = default,
        Guid? createdFromDocumentId = null,
        IReadOnlyList<Guid>? basedOnDocumentIds = null);

    Task UpdateDraftHeaderAsync(
        Guid documentId,
        GeneralJournalEntryDraftHeaderUpdate update,
        string updatedBy,
        CancellationToken ct = default);

    Task ReplaceDraftLinesAsync(
        Guid documentId,
        IReadOnlyList<GeneralJournalEntryDraftLineInput> lines,
        string updatedBy,
        CancellationToken ct = default);

    Task SubmitAsync(Guid documentId, string submittedBy, CancellationToken ct = default);

    Task ApproveAsync(Guid documentId, string approvedBy, CancellationToken ct = default);

    Task RejectAsync(Guid documentId, string rejectedBy, string rejectReason, CancellationToken ct = default);

    Task PostApprovedAsync(Guid documentId, string postedBy, CancellationToken ct = default);

    /// <summary>
    /// Creates a system reversal of a previously posted document.
    ///
    /// - The reversal is created as a new GJE with Source=System and ApprovalState=Approved.
    /// - By default it is posted immediately (subject to closed-period rules).
    /// - The method is idempotent: only one reversal document is allowed per original.
    /// </summary>
    Task<Guid> ReversePostedAsync(
        Guid originalDocumentId,
        DateTime reversalDateUtc,
        string initiatedBy,
        bool postImmediately = true,
        CancellationToken ct = default);
}
