using System.Collections.Concurrent;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Caches immutable operational-register physical read metadata across request scopes.
/// Missing physical tables are deliberately not cached so schema creation becomes visible immediately.
/// </summary>
public sealed class OperationalRegisterReadContextCache(TimeProvider timeProvider)
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<CacheKey, CacheEntry> _entries = new();
    private readonly ConcurrentDictionary<CacheKey, SemaphoreSlim> _loadGates = new();

    public async Task<OperationalRegisterReadContext> GetOrCreateAsync(
        Guid registerId,
        string requiredResourceColumn,
        Func<CancellationToken, Task<OperationalRegisterReadContext>> factory,
        CancellationToken ct)
    {
        var key = new CacheKey(registerId, requiredResourceColumn);
        if (TryGet(key, out var cached))
            return cached;

        var gate = _loadGates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            if (TryGet(key, out cached))
                return cached;

            var created = await factory(ct);
            if (created.MovementsExist && created.BalancesExist)
                _entries[key] = new CacheEntry(created, timeProvider.GetUtcNow().Add(TimeToLive));

            return created;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Invalidate(Guid registerId)
    {
        foreach (var key in _entries.Keys)
        {
            if (key.RegisterId == registerId)
                _entries.TryRemove(key, out _);
        }
    }

    private bool TryGet(CacheKey key, out OperationalRegisterReadContext context)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAtUtc > timeProvider.GetUtcNow())
            {
                context = entry.Context;
                return true;
            }

            _entries.TryRemove(key, out _);
        }

        context = null!;
        return false;
    }

    private readonly record struct CacheKey(Guid RegisterId, string RequiredResourceColumn);

    private sealed record CacheEntry(OperationalRegisterReadContext Context, DateTimeOffset ExpiresAtUtc);
}

public sealed record OperationalRegisterReadContext(
    string MovementsTable,
    string BalancesTable,
    bool MovementsExist,
    bool BalancesExist);
