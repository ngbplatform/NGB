using NGB.Accounting.Accounts;

namespace NGB.Accounting.Reports.BalanceSheet;

public sealed class BalanceSheetSection
{
    public StatementSection Section { get; init; }
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<BalanceSheetLine> Lines { get; init; } = [];
    public decimal Total { get; init; }
}
