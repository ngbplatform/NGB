using NGB.Accounting.Accounts;

namespace NGB.Persistence.Readers.Accounts;

/// <summary>
/// Lightweight bulk lookup for account labels used by UI/report enrichment.
/// </summary>
public interface IAccountLookupReader
{
    Task<IReadOnlyList<AccountLookupRecord>> GetByIdsAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken ct = default);
}
