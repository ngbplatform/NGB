namespace NGB.BackgroundJobs.Contracts;

/// <summary>
/// Executable job implementation resolved from DI by <see cref="JobId"/>.
///
/// Implementations should be:
///  - idempotent / no-op safe
///  - bounded per run (work in chunks)
///  - concurrency-safe (often via Postgres advisory locks, per key)
/// </summary>
public interface IPlatformBackgroundJob
{
    string JobId { get; }
    Task RunAsync(CancellationToken cancellationToken);
}
