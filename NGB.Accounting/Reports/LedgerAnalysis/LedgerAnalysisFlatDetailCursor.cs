namespace NGB.Accounting.Reports.LedgerAnalysis;

public sealed record LedgerAnalysisFlatDetailCursor(
    DateTime AfterPeriodUtc,
    long AfterEntryId,
    string AfterPostingSide);
