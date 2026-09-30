namespace NGB.Persistence.Locks;

/// <summary>Separate finalizer serialization and queued, database-visible publication waits.</summary>
public interface IOperationalRegisterFinalizationLockManager
{
    Task LockOperationalRegisterFinalizationAsync(Guid registerId, CancellationToken ct = default);

    /// <summary>
    /// Acquires the normal register and operational-month locks in that order. Uses database
    /// waiting so an already queued publisher is not overtaken by a stream of try-lock writers,
    /// and PostgreSQL can detect cycles with DDL or external transactions. Requires a deadline.
    /// </summary>
    Task LockOperationalRegisterPublicationAsync(Guid registerId, DateOnly periodMonth, CancellationToken ct);
}
