namespace NGB.Core.Documents.GeneralJournalEntry;

/// <summary>
/// General Journal Entry (GJE) is the platform-level manual journal document.
///
/// Storage model:
/// - documents              : common registry (id, type_code, date_utc, status)
/// - doc_general_journal_entry        : typed header + approval/audit
/// - doc_general_journal_entry__lines : user-entered lines (debit/credit)
/// - doc_general_journal_entry__allocations : deterministic debit/credit pairing saved at posting time
///
/// Notes:
/// - Approval workflow is stored in the typed header. DocumentStatus remains Draft/Posted.
/// - System reversals are stored as GJE documents with Source=System and ApprovalState=Approved.
/// </summary>
public static class GeneralJournalEntryModels
{
    public enum JournalType : short
    {
        Standard = 1,
        Reversing = 2,
        Adjusting = 3,
        Opening = 4,
        Closing = 5
    }

    public enum Source : short
    {
        Manual = 1,
        System = 2
    }

    public enum ApprovalState : short
    {
        Draft = 1,
        Submitted = 2,
        Approved = 3,
        Rejected = 4
    }

    public enum LineSide : short
    {
        Debit = 1,
        Credit = 2
    }
}

public sealed record GeneralJournalEntryHeaderRecord(
    Guid DocumentId,
    GeneralJournalEntryModels.JournalType JournalType,
    GeneralJournalEntryModels.Source Source,
    GeneralJournalEntryModels.ApprovalState ApprovalState,
    string? ReasonCode,
    string? Memo,
    string? ExternalReference,
    bool AutoReverse,
    DateOnly? AutoReverseOnUtc,
    Guid? ReversalOfDocumentId,
    string? InitiatedBy,
    DateTime? InitiatedAtUtc,
    string? SubmittedBy,
    DateTime? SubmittedAtUtc,
    string? ApprovedBy,
    DateTime? ApprovedAtUtc,
    string? RejectedBy,
    DateTime? RejectedAtUtc,
    string? RejectReason,
    string? PostedBy,
    DateTime? PostedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GeneralJournalEntryLineRecord(
    Guid DocumentId,
    int LineNo,
    GeneralJournalEntryModels.LineSide Side,
    Guid AccountId,
    decimal Amount,
    string? Memo,
    Guid DimensionSetId = default);

public sealed record GeneralJournalEntryAllocationRecord(
    Guid DocumentId,
    int EntryNo,
    int DebitLineNo,
    int CreditLineNo,
    decimal Amount);
