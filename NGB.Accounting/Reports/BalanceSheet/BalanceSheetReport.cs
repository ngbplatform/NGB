namespace NGB.Accounting.Reports.BalanceSheet;

public sealed class BalanceSheetReport
{
    public DateOnly AsOfPeriod { get; init; }

    public IReadOnlyList<BalanceSheetSection> Sections { get; init; } = [];

    public decimal TotalAssets { get; init; }
    public decimal TotalLiabilities { get; init; }
    public decimal TotalEquity { get; init; }
    public decimal TotalLiabilitiesAndEquity { get; init; }

    /// <summary>
    /// TotalAssets - (TotalLiabilities + TotalEquity).
    /// </summary>
    public decimal Difference { get; init; }

    public bool IsBalanced { get; init; }
}
