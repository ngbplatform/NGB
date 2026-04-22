using System.Collections.Concurrent;
using NGB.BackgroundJobs.Contracts;

namespace NGB.BackgroundJobs.Observability;

internal sealed class JobRunMetrics : IJobRunMetrics
{
    private readonly ConcurrentDictionary<string, long> _counters = new(StringComparer.Ordinal);

    public void Increment(string name, long delta = 1)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        
        if (delta == 0)
            return;

        _counters.AddOrUpdate(name.Trim(), delta, (_, existing) => existing + delta);
    }

    public void Set(string name, long value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        _counters[name.Trim()] = value;
    }

    public IReadOnlyDictionary<string, long> Snapshot()
    {
        // Return an immutable snapshot to avoid concurrent enumeration issues in logs.
        return _counters.Count == 0
            ? new Dictionary<string, long>(0)
            : new Dictionary<string, long>(_counters);
    }
}
