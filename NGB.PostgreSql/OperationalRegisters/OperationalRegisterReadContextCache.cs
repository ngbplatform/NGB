using NGB.PostgreSql.Internal;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Caches immutable operational-register physical read metadata across request scopes.
/// Missing physical tables are deliberately not cached so schema creation becomes visible immediately.
/// </summary>
public sealed class OperationalRegisterReadContextCache
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);
    private const int DefaultCapacity = 4_096;
    private readonly TimeProvider _timeProvider;
    private readonly BoundedExpiringCache<CacheKey, OperationalRegisterReadContext> _entries;
    private readonly AsyncKeyedLock<CacheKey> _loadGates = new();

    public OperationalRegisterReadContextCache(TimeProvider timeProvider)
        : this(timeProvider, DefaultCapacity)
    {
    }

    internal OperationalRegisterReadContextCache(TimeProvider timeProvider, int capacity)
    {
        _timeProvider = timeProvider;
        _entries = new BoundedExpiringCache<CacheKey, OperationalRegisterReadContext>(capacity);
    }

    internal int EntryCount => _entries.Count;
    internal int LoadGateCount => _loadGates.Count;

    public async Task<OperationalRegisterReadContext> GetOrCreateAsync(
        Guid registerId,
        string requiredResourceColumn,
        Func<CancellationToken, Task<OperationalRegisterReadContext>> factory,
        CancellationToken ct)
    {
        var key = new CacheKey(registerId, requiredResourceColumn);
        if (TryGet(key, out var cached))
            return cached;

        using (await _loadGates.AcquireAsync(key, ct))
        {
            if (TryGet(key, out cached))
                return cached;

            var created = await factory(ct);
            if (created.MovementsExist && created.BalancesExist)
            {
                var now = _timeProvider.GetUtcNow();
                _entries.Set(key, created, now.Add(TimeToLive), now);
            }

            return created;
        }
    }

    public void Invalidate(Guid registerId)
    {
        _entries.RemoveWhere(key => key.RegisterId == registerId);
    }

    private bool TryGet(CacheKey key, out OperationalRegisterReadContext context)
    {
        return _entries.TryGet(key, _timeProvider.GetUtcNow(), out context);
    }

    private readonly record struct CacheKey(Guid RegisterId, string RequiredResourceColumn);

}

public sealed record OperationalRegisterReadContext(
    string MovementsTable,
    string BalancesTable,
    bool MovementsExist,
    bool BalancesExist);
