namespace NGB.Accounting.Reports.GeneralJournal;

/// <summary>
/// Keyset pagination cursor for General Journal, ordered by (PeriodUtc, EntryId).
/// </summary>
public sealed record GeneralJournalCursor(DateTime AfterPeriodUtc, long AfterEntryId);
