using System.Collections.Concurrent;

namespace NGB.PostgreSql.Schema;

/// <summary>
/// Short-lived positive cache for verified dynamic-table shapes. Failed probes are never cached.
/// A shape fingerprint prevents metadata changes from reusing an incompatible verification.
/// </summary>
public sealed class PostgresRelationShapeCache(TimeProvider timeProvider)
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<CacheKey, DateTimeOffset> _verified = new();
    private readonly ConcurrentDictionary<CacheKey, SemaphoreSlim> _probeGates = new();

    public async Task<bool> IsVerifiedAsync(
        string relationName,
        string shapeFingerprint,
        Func<CancellationToken, Task<bool>> probe,
        CancellationToken ct)
    {
        var key = new CacheKey(relationName, shapeFingerprint);
        if (IsCurrent(key))
            return true;

        var gate = _probeGates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            if (IsCurrent(key))
                return true;

            if (!await probe(ct))
                return false;

            _verified[key] = timeProvider.GetUtcNow().Add(TimeToLive);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public void MarkVerified(string relationName, string shapeFingerprint)
        => _verified[new CacheKey(relationName, shapeFingerprint)] = timeProvider.GetUtcNow().Add(TimeToLive);

    public void Invalidate(string relationName)
    {
        foreach (var key in _verified.Keys)
        {
            if (string.Equals(key.RelationName, relationName, StringComparison.Ordinal))
                _verified.TryRemove(key, out _);
        }
    }

    private bool IsCurrent(CacheKey key)
    {
        if (!_verified.TryGetValue(key, out var expiresAtUtc))
            return false;

        if (expiresAtUtc > timeProvider.GetUtcNow())
            return true;

        _verified.TryRemove(new KeyValuePair<CacheKey, DateTimeOffset>(key, expiresAtUtc));
        return false;
    }

    private readonly record struct CacheKey(string RelationName, string ShapeFingerprint);
}
