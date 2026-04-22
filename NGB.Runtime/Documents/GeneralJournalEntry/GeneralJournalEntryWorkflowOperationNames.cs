namespace NGB.Runtime.Documents.GeneralJournalEntry;

/// <summary>
/// Canonical operation names for workflow lifecycle logs.
/// Keep these stable for log-based assertions and operational observability.
/// </summary>
public static class GeneralJournalEntryWorkflowOperationNames
{
    public const string GetDraft = "GeneralJournalEntry.GetDraft";
    public const string CreateDraft = "GeneralJournalEntry.CreateDraft";
    public const string CreateAndPostApproved = "GeneralJournalEntry.CreateAndPostApproved";
    public const string UpdateDraftHeader = "GeneralJournalEntry.UpdateDraftHeader";
    public const string ReplaceDraftLines = "GeneralJournalEntry.ReplaceDraftLines";
    public const string Submit = "GeneralJournalEntry.Submit";
    public const string Approve = "GeneralJournalEntry.Approve";
    public const string Reject = "GeneralJournalEntry.Reject";
    public const string PostApproved = "GeneralJournalEntry.PostApproved";
    public const string ReversePosted = "GeneralJournalEntry.ReversePosted";
}
