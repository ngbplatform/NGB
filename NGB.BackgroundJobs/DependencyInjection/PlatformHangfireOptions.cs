namespace NGB.BackgroundJobs.DependencyInjection;

/// <summary>
/// Hangfire configuration used by NGB.BackgroundJobs.
///
/// Note: Background jobs are scheduled/processed in UTC by default.
/// </summary>
public sealed class PlatformHangfireOptions
{
    /// <summary>
    /// Provider connection string used by an optional batched storage inspector.
    /// Job execution itself uses the <c>JobStorage</c> supplied by composition.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Provider storage namespace used by an optional batched storage inspector.
    /// </summary>
    public string StorageNamespace { get; set; } = "hangfire";

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
