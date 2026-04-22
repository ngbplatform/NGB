namespace NGB.BackgroundJobs.DependencyInjection;

/// <summary>
/// Hangfire configuration used by NGB.BackgroundJobs.
///
/// Note: Background jobs are scheduled/processed in UTC by default.
/// </summary>
public sealed class PlatformHangfireOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Allows Hangfire.PostgreSql to install/upgrade its schema at runtime.
    /// Defaults to true for developer convenience.
    /// </summary>
    public bool PrepareSchemaIfNecessary { get; set; } = true;

    public int WorkerCount { get; set; } = Math.Max(1, Environment.ProcessorCount);

    public string[] Queues { get; set; } = ["default"];

    /// <summary>
    /// How long a job will wait to acquire a Hangfire distributed lock for its JobId.
    ///
    /// If the lock cannot be acquired within this time, the run is skipped (no exception thrown)
    /// to avoid overlap and backlog when a previous run is still executing.
    /// </summary>
    public int DistributedLockTimeoutSeconds { get; set; } = 1;

    /// <summary>
    /// If null, Hangfire will use its default server name.
    /// </summary>
    public string? ServerName { get; set; }
}
