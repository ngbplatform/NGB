namespace NGB.PostgreSql.Migrations.Evolve;

/// <summary>
/// Controls how the migrator behaves when a schema migration lock is already held.
/// </summary>
public enum SchemaMigrationLockMode
{
    /// <summary>
    /// Wait until the lock is acquired (optionally bounded by a timeout).
    /// </summary>
    Wait = 0,

    /// <summary>
    /// Try once and fail if the lock is not acquired.
    /// </summary>
    Try = 1,

    /// <summary>
    /// Try once and skip all migration work (success exit code) if the lock is not acquired.
    /// Useful for CronJobs where overlapping runs should be a no-op.
    /// </summary>
    Skip = 2,
}
