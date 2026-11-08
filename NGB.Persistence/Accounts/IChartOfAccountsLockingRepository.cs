namespace NGB.Persistence.Accounts;

/// <summary>
/// Optional capability for serializing account mutations with accounting entry writes.
/// </summary>
public interface IChartOfAccountsLockingRepository : IChartOfAccountsRepository
{
    /// <summary>
    /// Locks the account until the caller's transaction completes.
    /// Acquire before reading movement-dependent mutation prerequisites.
    /// </summary>
    Task LockForUpdateAsync(Guid accountId, CancellationToken ct = default);
}
