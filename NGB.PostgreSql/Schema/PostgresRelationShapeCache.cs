using NGB.PostgreSql.Internal;

namespace NGB.PostgreSql.Schema;

/// <summary>
/// Short-lived positive cache for verified dynamic-table shapes. Failed probes are never cached.
/// A shape fingerprint prevents metadata changes from reusing an incompatible verification.
/// </summary>
public sealed class PostgresRelationShapeCache
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);
    private const int DefaultCapacity = 8_192;
    private readonly TimeProvider _timeProvider;
    private readonly BoundedExpiringCache<CacheKey, bool> _verified;
    private readonly AsyncKeyedLock<CacheKey> _probeGates = new();

    public PostgresRelationShapeCache(TimeProvider timeProvider)
        : this(timeProvider, DefaultCapacity)
    {
    }

    internal PostgresRelationShapeCache(TimeProvider timeProvider, int capacity)
    {
        _timeProvider = timeProvider;
        _verified = new BoundedExpiringCache<CacheKey, bool>(capacity);
    }

    internal int EntryCount => _verified.Count;
    internal int ProbeGateCount => _probeGates.Count;

    public async Task<bool> IsVerifiedAsync(
        string relationName,
        string shapeFingerprint,
        Func<CancellationToken, Task<bool>> probe,
        CancellationToken ct)
    {
        var key = new CacheKey(relationName, shapeFingerprint);
        if (IsCurrent(key))
            return true;

        using (await _probeGates.AcquireAsync(key, ct))
        {
            if (IsCurrent(key))
                return true;

            if (!await probe(ct))
                return false;

            Remember(key);
            return true;
        }
    }

    public void MarkVerified(string relationName, string shapeFingerprint)
        => Remember(new CacheKey(relationName, shapeFingerprint));

    public void Invalidate(string relationName)
    {
        _verified.RemoveWhere(key => string.Equals(key.RelationName, relationName, StringComparison.Ordinal));
    }

    private bool IsCurrent(CacheKey key)
    {
        return _verified.TryGet(key, _timeProvider.GetUtcNow(), out _);
    }

    private void Remember(CacheKey key)
    {
        var now = _timeProvider.GetUtcNow();
        _verified.Set(key, true, now.Add(TimeToLive), now);
    }

    private readonly record struct CacheKey(string RelationName, string ShapeFingerprint);
}
