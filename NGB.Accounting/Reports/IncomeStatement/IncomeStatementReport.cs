namespace NGB.Accounting.Reports.IncomeStatement;

public sealed class IncomeStatementReport
{
    public required DateOnly FromInclusive { get; init; }
    public required DateOnly ToInclusive { get; init; }

    public required IReadOnlyList<IncomeStatementSection> Sections { get; init; }

    public required decimal TotalIncome { get; init; }
    public required decimal TotalExpenses { get; init; }
    public required decimal TotalOtherIncome { get; init; }
    public required decimal TotalOtherExpense { get; init; }

    public required decimal NetIncome { get; init; }
}
