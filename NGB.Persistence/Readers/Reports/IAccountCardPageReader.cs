using NGB.Accounting.Reports.AccountCard;

namespace NGB.Persistence.Readers.Reports;

/// <summary>
/// Low-level paged reader for account card lines (keyset pagination).
/// </summary>
public interface IAccountCardPageReader
{
    Task<AccountCardLinePage> GetPageAsync(AccountCardLinePageRequest request, CancellationToken ct = default);
}
