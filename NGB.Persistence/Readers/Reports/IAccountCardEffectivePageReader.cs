using NGB.Accounting.Reports.AccountCard;

namespace NGB.Persistence.Readers.Reports;

/// <summary>
/// Low-level reader for the canonical Account Card effective stream.
/// Unlike <see cref="IAccountCardPageReader"/>, this reader returns already-reduced effective lines,
/// suitable for cursor paging in the canonical report UI.
/// When requested, the returned page can also carry grand totals for the whole filtered range.
/// </summary>
public interface IAccountCardEffectivePageReader
{
    Task<AccountCardLinePage> GetPageAsync(AccountCardLinePageRequest request, CancellationToken ct = default);
}
