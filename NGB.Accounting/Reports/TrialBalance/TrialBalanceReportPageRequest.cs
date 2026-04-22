using NGB.Core.Dimensions;

namespace NGB.Accounting.Reports.TrialBalance;

/// <summary>
/// Canonical Trial Balance request envelope.
/// Offset/Limit are retained for contract stability, but runtime executes the report as a bounded summary.
/// </summary>
public sealed class TrialBalanceReportPageRequest
{
    public DateOnly FromInclusive { get; init; }
    public DateOnly ToInclusive { get; init; }
    public DimensionScopeBag? DimensionScopes { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public bool ShowSubtotals { get; init; } = true;
}
