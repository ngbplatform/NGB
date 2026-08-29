using NGB.Accounting.Reports.AccountCard;
using NGB.Core.Dimensions;

namespace NGB.Persistence.Readers.Reports;

/// <summary>
/// Low-level reader for the canonical Account Card effective stream.
/// Unlike <see cref="IAccountCardPageReader"/>, this reader returns already-reduced effective lines,
/// suitable for cursor paging in the canonical report UI.
/// When requested, the returned page can also carry grand totals for the whole filtered range.
/// </summary>
public interface IAccountCardEffectivePageReader
{
    /// <summary>
    /// Computes the balance immediately before <paramref name="fromInclusive"/> at the database,
    /// already restricted to one account and the requested dimension scope.
    /// </summary>
    Task<decimal> GetOpeningBalanceAsync(
        Guid accountId,
        DateOnly fromInclusive,
        DimensionScopeBag? dimensionScopes,
        CancellationToken ct = default);

    Task<AccountCardLinePage> GetPageAsync(AccountCardLinePageRequest request, CancellationToken ct = default);
}
