namespace NGB.BackgroundJobs.Contracts;

/// <summary>
/// Lightweight in-process metrics bag for a single background job run.
///
/// The runner publishes a structured JobRunSummary log at the end of execution
/// and includes these counters.
/// </summary>
public interface IJobRunMetrics
{
    void Increment(string name, long delta = 1);

    void Set(string name, long value);

    IReadOnlyDictionary<string, long> Snapshot();
}
