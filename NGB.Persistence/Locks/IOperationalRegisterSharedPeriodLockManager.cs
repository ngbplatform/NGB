namespace NGB.Persistence.Locks;

/// <summary>
/// Optional provider capability for avoiding cross-register month-lock inversions.
/// Register mutation remains exclusive; shared month locks still respect external
/// exclusive operational-month guards.
/// </summary>
public interface IOperationalRegisterSharedPeriodLockManager
    : IAdvisoryLockManager, IOperationalRegisterFinalizationLockManager
{
    /// <summary>Requires the affected register's exclusive lock in the current transaction.</summary>
    Task LockOperationalRegisterSharedPeriodsAsync(
        IReadOnlyCollection<DateOnly> periods,
        CancellationToken ct = default);

    /// <summary>Queues for the exclusive register lock, then takes a shared month lock.</summary>
    Task LockOperationalRegisterSharedPublicationAsync(
        Guid registerId,
        DateOnly periodMonth,
        CancellationToken ct);
}
