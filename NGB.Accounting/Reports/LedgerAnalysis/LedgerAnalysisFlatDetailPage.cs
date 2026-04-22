namespace NGB.Accounting.Reports.LedgerAnalysis;

public sealed record LedgerAnalysisFlatDetailPage(
    IReadOnlyList<LedgerAnalysisFlatDetailRow> Rows,
    bool HasMore,
    LedgerAnalysisFlatDetailCursor? NextCursor);

public sealed record LedgerAnalysisFlatDetailRow(IReadOnlyDictionary<string, object?> Values);
