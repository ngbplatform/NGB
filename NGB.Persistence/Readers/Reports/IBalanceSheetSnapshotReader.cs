using NGB.Core.Dimensions;

namespace NGB.Persistence.Readers.Reports;

/// <summary>
/// Specialized read-side boundary for Balance Sheet snapshot aggregation.
/// Returns account-level closing balances already aggregated "as of" the requested month.
/// </summary>
public interface IBalanceSheetSnapshotReader
{
    Task<BalanceSheetSnapshot> GetAsync(
        DateOnly asOfPeriod,
        DimensionScopeBag? dimensionScopes,
        CancellationToken ct = default);
}

public sealed record BalanceSheetSnapshot(
    IReadOnlyList<BalanceSheetSnapshotRow> Rows,
    DateOnly? LatestClosedPeriod,
    int RollForwardPeriods);

public sealed record BalanceSheetSnapshotRow(Guid AccountId, decimal ClosingBalance);
