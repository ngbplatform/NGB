using NGB.Core.Dimensions;

namespace NGB.Accounting.Reports.BalanceSheet;

/// <summary>
/// Balance Sheet request.
/// The report is point-in-time: "as of" the end of the selected month.
/// </summary>
public sealed class BalanceSheetReportRequest
{
    /// <summary>
    /// Report month start (inclusive). Report is "as of" month end.
    /// </summary>
    public DateOnly AsOfPeriod { get; init; }

    /// <summary>
    /// Optional multi-value dimension filter:
    /// OR within one dimension, AND across dimensions.
    /// </summary>
    public DimensionScopeBag? DimensionScopes { get; init; }

    /// <summary>
    /// Include accounts with a zero balance.
    /// </summary>
    public bool IncludeZeroAccounts { get; init; }

    /// <summary>
    /// If true, adds a synthetic Equity line "Net Income" computed from P&amp;L sections.
    /// This makes the Balance Sheet balance even when income/expense accounts are not closed yet.
    /// </summary>
    public bool IncludeNetIncomeInEquity { get; init; }
}
