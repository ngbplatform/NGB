using NGB.Core.Dimensions;

namespace NGB.Persistence.Readers.Reports;

/// <summary>
/// Specialized read-side boundary for Trial Balance aggregation.
/// Returns rows already aggregated by AccountId + DimensionSetId for the requested range.
/// </summary>
public interface ITrialBalanceSnapshotReader
{
    Task<TrialBalanceSnapshot> GetAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes,
        CancellationToken ct = default);
}

public sealed record TrialBalanceSnapshot(IReadOnlyList<TrialBalanceSnapshotRow> Rows);

public sealed record TrialBalanceSnapshotRow(
    Guid AccountId,
    string AccountCode,
    Guid DimensionSetId,
    decimal OpeningBalance,
    decimal DebitAmount,
    decimal CreditAmount);
