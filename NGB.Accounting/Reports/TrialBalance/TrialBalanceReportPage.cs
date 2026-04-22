namespace NGB.Accounting.Reports.TrialBalance;

public sealed record TrialBalanceReportPage(
    IReadOnlyList<TrialBalanceReportRow> Rows,
    int Total,
    bool HasMore,
    TrialBalanceReportTotals Totals);
