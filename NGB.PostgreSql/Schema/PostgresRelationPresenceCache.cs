using System.Collections.Concurrent;

namespace NGB.PostgreSql.Schema;

/// <summary>
/// Caches positive to_regclass probes across request scopes. Negative results are not cached,
/// so newly created dynamic tables are visible immediately.
/// </summary>
public sealed class PostgresRelationPresenceCache(TimeProvider timeProvider)
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _present = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public async Task<bool> ExistsAsync(
        string relationName,
        Func<CancellationToken, Task<bool>> probe,
        CancellationToken ct)
    {
        if (IsPresent(relationName))
            return true;

        var gate = _gates.GetOrAdd(relationName, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            if (IsPresent(relationName))
                return true;

            var exists = await probe(ct);
            if (exists)
                _present[relationName] = timeProvider.GetUtcNow().Add(TimeToLive);

            return exists;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Invalidate(string relationName) => _present.TryRemove(relationName, out _);

    private bool IsPresent(string relationName)
    {
        if (!_present.TryGetValue(relationName, out var expiresAtUtc))
            return false;

        if (expiresAtUtc > timeProvider.GetUtcNow())
            return true;

        _present.TryRemove(new KeyValuePair<string, DateTimeOffset>(relationName, expiresAtUtc));
        return false;
    }
}
