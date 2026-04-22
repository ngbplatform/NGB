namespace NGB.Accounting.Reports.IncomeStatement;

public sealed class IncomeStatementLine
{
    public required Guid AccountId { get; init; }
    public required string AccountCode { get; init; }
    public required string AccountName { get; init; }

    /// <summary>
    /// Signed amount in reporting convention for the section.
    /// Revenue/Income is positive, Expenses are positive.
    /// </summary>
    public required decimal Amount { get; init; }
}
