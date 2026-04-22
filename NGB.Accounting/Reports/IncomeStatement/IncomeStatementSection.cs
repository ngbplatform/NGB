using NGB.Accounting.Accounts;

namespace NGB.Accounting.Reports.IncomeStatement;

public sealed class IncomeStatementSection
{
    public required StatementSection Section { get; init; }
    public required IReadOnlyList<IncomeStatementLine> Lines { get; init; }
    public required decimal Total { get; init; }
}
