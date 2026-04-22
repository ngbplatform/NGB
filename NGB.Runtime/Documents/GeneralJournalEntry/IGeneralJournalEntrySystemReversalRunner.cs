namespace NGB.Runtime.Documents.GeneralJournalEntry;

public interface IGeneralJournalEntrySystemReversalRunner
{
    /// <summary>
    /// Posts due system reversals (Source=System, JournalType=Reversing, ApprovalState=Approved).
    /// Intended to be invoked by an external scheduler.
    /// </summary>
    Task<int> PostDueSystemReversalsAsync(
        DateOnly utcDate,
        int batchSize,
        string postedBy = "SYSTEM",
        CancellationToken ct = default);
}
