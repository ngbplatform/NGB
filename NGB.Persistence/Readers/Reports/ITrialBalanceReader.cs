using NGB.Accounting.Reports.TrialBalance;
using NGB.Core.Dimensions;

namespace NGB.Persistence.Readers.Reports;

public interface ITrialBalanceReader
{
    /// <summary>
    /// Returns trial balance for the given range.
    /// Periods are month starts (DateOnly, first day of month).
    /// The result is aggregated by AccountId + DimensionSetId.
    /// </summary>
    Task<IReadOnlyList<TrialBalanceRow>> GetAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct = default);

    /// <summary>
    /// Returns trial balance for the given range filtered by multi-value dimension scopes.
    /// OR within one dimension, AND across dimensions.
    /// </summary>
    Task<IReadOnlyList<TrialBalanceRow>> GetAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes,
        CancellationToken ct = default);
}
