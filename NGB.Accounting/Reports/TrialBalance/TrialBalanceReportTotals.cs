namespace NGB.Accounting.Reports.TrialBalance;

public sealed record TrialBalanceReportTotals(
    decimal OpeningBalance,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal ClosingBalance);
