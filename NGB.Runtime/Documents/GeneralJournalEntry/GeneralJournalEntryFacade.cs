namespace NGB.Runtime.Documents.GeneralJournalEntry;

/// <summary>
/// Thin DX facade over <see cref="IGeneralJournalEntryDocumentService"/>.
///
/// - No runtime magic.
/// - No business logic duplication (delegates to the document service).
/// - Adds convenience composition methods for common flows.
/// </summary>
public interface IGeneralJournalEntryFacade
{
    Task<Guid> CreateDraftAsync(
        DateTime dateUtc,
        string initiatedBy,
        CancellationToken ct = default,
        Guid? createdFromDocumentId = null,
        IReadOnlyList<Guid>? basedOnDocumentIds = null);

    Task<GeneralJournalEntryDraftSnapshot> GetDraftAsync(Guid documentId, CancellationToken ct = default);

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
    /// Convenience composition:
    /// Create Draft → (optional header/lines) → Submit → Approve → Post.
    ///
    /// All business rules are enforced by the underlying document service.
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

    Task<Guid> ReversePostedAsync(
        Guid originalDocumentId,
        DateTime reversalDateUtc,
        string initiatedBy,
        bool postImmediately = true,
        CancellationToken ct = default);
}

public sealed class GeneralJournalEntryFacade(IGeneralJournalEntryDocumentService service) : IGeneralJournalEntryFacade
{
    public Task<Guid> CreateDraftAsync(
        DateTime dateUtc,
        string initiatedBy,
        CancellationToken ct = default,
        Guid? createdFromDocumentId = null,
        IReadOnlyList<Guid>? basedOnDocumentIds = null)
        => service.CreateDraftAsync(dateUtc, initiatedBy, ct, createdFromDocumentId, basedOnDocumentIds);

    public Task<GeneralJournalEntryDraftSnapshot> GetDraftAsync(Guid documentId, CancellationToken ct = default)
        => service.GetDraftAsync(documentId, ct);

    public Task UpdateDraftHeaderAsync(
        Guid documentId,
        GeneralJournalEntryDraftHeaderUpdate update,
        string updatedBy,
        CancellationToken ct = default)
        => service.UpdateDraftHeaderAsync(documentId, update, updatedBy, ct);

    public Task ReplaceDraftLinesAsync(
        Guid documentId,
        IReadOnlyList<GeneralJournalEntryDraftLineInput> lines,
        string updatedBy,
        CancellationToken ct = default)
        => service.ReplaceDraftLinesAsync(documentId, lines, updatedBy, ct);

    public Task SubmitAsync(Guid documentId, string submittedBy, CancellationToken ct = default)
        => service.SubmitAsync(documentId, submittedBy, ct);

    public Task ApproveAsync(Guid documentId, string approvedBy, CancellationToken ct = default)
        => service.ApproveAsync(documentId, approvedBy, ct);

    public Task RejectAsync(Guid documentId, string rejectedBy, string rejectReason, CancellationToken ct = default)
        => service.RejectAsync(documentId, rejectedBy, rejectReason, ct);

    public Task PostApprovedAsync(Guid documentId, string postedBy, CancellationToken ct = default)
        => service.PostApprovedAsync(documentId, postedBy, ct);

    public Task<Guid> CreateAndPostApprovedAsync(
        DateTime dateUtc,
        GeneralJournalEntryDraftHeaderUpdate? header,
        IReadOnlyList<GeneralJournalEntryDraftLineInput>? lines,
        string initiatedBy,
        string submittedBy,
        string approvedBy,
        string postedBy,
        CancellationToken ct = default,
        Guid? createdFromDocumentId = null,
        IReadOnlyList<Guid>? basedOnDocumentIds = null)
        => service.CreateAndPostApprovedAsync(dateUtc, header, lines, initiatedBy, submittedBy, approvedBy, postedBy, ct, createdFromDocumentId, basedOnDocumentIds);

    public Task<Guid> ReversePostedAsync(
        Guid originalDocumentId,
        DateTime reversalDateUtc,
        string initiatedBy,
        bool postImmediately = true,
        CancellationToken ct = default)
        => service.ReversePostedAsync(originalDocumentId, reversalDateUtc, initiatedBy, postImmediately, ct);

}
