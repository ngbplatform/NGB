using NGB.PostgreSql.Internal;

namespace NGB.PostgreSql.Schema;

/// <summary>
/// Caches positive to_regclass probes across request scopes. Negative results are not cached,
/// so newly created dynamic tables are visible immediately.
/// </summary>
public sealed class PostgresRelationPresenceCache
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);
    private const int DefaultCapacity = 8_192;
    private readonly TimeProvider _timeProvider;
    private readonly BoundedExpiringCache<string, bool> _present;
    private readonly AsyncKeyedLock<string> _gates = new(StringComparer.Ordinal);

    public PostgresRelationPresenceCache(TimeProvider timeProvider)
        : this(timeProvider, DefaultCapacity)
    {
    }

    internal PostgresRelationPresenceCache(TimeProvider timeProvider, int capacity)
    {
        _timeProvider = timeProvider;
        _present = new BoundedExpiringCache<string, bool>(capacity, StringComparer.Ordinal);
    }

    internal int EntryCount => _present.Count;
    internal int GateCount => _gates.Count;

    public async Task<bool> ExistsAsync(
        string relationName,
        Func<CancellationToken, Task<bool>> probe,
        CancellationToken ct)
    {
        if (IsPresent(relationName))
            return true;

        using (await _gates.AcquireAsync(relationName, ct))
        {
            if (IsPresent(relationName))
                return true;

            var exists = await probe(ct);
            if (exists)
            {
                var now = _timeProvider.GetUtcNow();
                _present.Set(relationName, true, now.Add(TimeToLive), now);
            }

            return exists;
        }
    }

    public void Invalidate(string relationName) => _present.Remove(relationName);

    private bool IsPresent(string relationName)
    {
        return _present.TryGet(relationName, _timeProvider.GetUtcNow(), out _);
    }
}
