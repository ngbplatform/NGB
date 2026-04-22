namespace NGB.Accounting.Reports.TrialBalance;

public sealed record TrialBalanceReportRow(
    TrialBalanceReportRowKind RowKind,
    string AccountDisplay,
    decimal OpeningBalance,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal ClosingBalance,
    int OutlineLevel = 0,
    string? GroupKey = null,
    Guid? AccountId = null);
