using NGB.Accounting.Accounts;

namespace NGB.Persistence.Readers.Accounts;

/// <summary>
/// Narrow lookup reader for eligible retained earnings accounts used by fiscal-year close UX.
/// </summary>
public interface IRetainedEarningsAccountLookupReader
{
    Task<IReadOnlyList<AccountLookupRecord>> SearchAsync(string? query, int limit, CancellationToken ct = default);
}
