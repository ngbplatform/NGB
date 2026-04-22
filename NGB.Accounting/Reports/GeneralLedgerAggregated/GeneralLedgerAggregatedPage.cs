namespace NGB.Accounting.Reports.GeneralLedgerAggregated;

public sealed record GeneralLedgerAggregatedPage(
    IReadOnlyList<GeneralLedgerAggregatedLine> Lines,
    bool HasMore,
    GeneralLedgerAggregatedLineCursor? NextCursor);
