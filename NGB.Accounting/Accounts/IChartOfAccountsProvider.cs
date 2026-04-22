namespace NGB.Accounting.Accounts;

/// <summary>
/// Provides a per-scope snapshot of the Chart of Accounts loaded from persistence.
/// New scopes see new snapshots (so changes become visible).
/// </summary>
public interface IChartOfAccountsProvider
{
    Task<ChartOfAccounts> GetAsync(CancellationToken ct = default);
}
