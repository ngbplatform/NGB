namespace NGB.Persistence.Locks;

/// <summary>
/// Coordinates extension of accounting history into earlier months with period close/reopen.
/// Extensions share a lock; chain changes take it exclusively. Existing-month writes retain
/// their ordinary month locks. All methods require a transaction.
/// </summary>
public interface IAccountingHistoryLockManager : IAdvisoryLockManager
{
    Task LockAccountingHistoryExtensionAsync(DateOnly earliestPeriod, CancellationToken ct = default);

    Task LockAccountingHistoryChangeAsync(CancellationToken ct = default);
}
