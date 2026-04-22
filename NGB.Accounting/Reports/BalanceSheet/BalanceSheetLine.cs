namespace NGB.Accounting.Reports.BalanceSheet;

public sealed class BalanceSheetLine
{
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;

    /// <summary>
    /// Display amount for statements (already normalized using NormalBalance and Contra).
    /// Convention: for Balance Sheet this is typically a non-negative number.
    /// </summary>
    public decimal Amount { get; init; }
}
