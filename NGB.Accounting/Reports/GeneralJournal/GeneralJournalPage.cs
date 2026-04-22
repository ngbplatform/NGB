namespace NGB.Accounting.Reports.GeneralJournal;

public sealed record GeneralJournalPage(
    IReadOnlyList<GeneralJournalLine> Lines,
    bool HasMore,
    GeneralJournalCursor? NextCursor);
