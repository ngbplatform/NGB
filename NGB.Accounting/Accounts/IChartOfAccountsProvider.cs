namespace NGB.Accounting.Accounts;

/// <summary>
/// Provides a Chart of Accounts snapshot pinned for the lifetime of the current scope.
/// Persisted snapshots may be shared between scopes and are invalidated by successful
/// Chart of Accounts management mutations.
/// </summary>
public interface IChartOfAccountsProvider
{
    Task<ChartOfAccounts> GetAsync(CancellationToken ct = default);
}
