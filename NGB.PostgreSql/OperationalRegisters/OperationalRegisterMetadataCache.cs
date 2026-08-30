using System.Collections.Concurrent;
using NGB.OperationalRegisters.Contracts;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Caches operational-register metadata only after the first movement makes that metadata
/// immutable at the database level. Mutable and missing registers are deliberately not cached.
/// </summary>
public sealed class OperationalRegisterMetadataCache(TimeProvider timeProvider)
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<Guid, CacheEntry> _entries = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _loadGates = new();

    public async Task<OperationalRegisterMetadataContext> GetOrCreateAsync(
        Guid registerId,
        Func<CancellationToken, Task<OperationalRegisterMetadataContext>> factory,
        CancellationToken ct)
    {
        if (TryGet(registerId, out var cached))
            return cached;

        var gate = _loadGates.GetOrAdd(registerId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            if (TryGet(registerId, out cached))
                return cached;

            var created = await factory(ct);
            Remember(created);
            return created;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Remember(OperationalRegisterMetadataContext context)
    {
        if (context.Register.HasMovements)
            _entries[context.Register.RegisterId] = new CacheEntry(context, timeProvider.GetUtcNow().Add(TimeToLive));
    }

    public void Invalidate(Guid registerId) => _entries.TryRemove(registerId, out _);

    private bool TryGet(Guid registerId, out OperationalRegisterMetadataContext context)
    {
        if (_entries.TryGetValue(registerId, out var entry))
        {
            if (entry.ExpiresAtUtc > timeProvider.GetUtcNow())
            {
                context = entry.Context;
                return true;
            }

            _entries.TryRemove(new KeyValuePair<Guid, CacheEntry>(registerId, entry));
        }

        context = null!;
        return false;
    }

    private sealed record CacheEntry(OperationalRegisterMetadataContext Context, DateTimeOffset ExpiresAtUtc);
}

public sealed record OperationalRegisterMetadataContext(
    OperationalRegisterAdminItem Register,
    OperationalRegisterResource[] Resources,
    string MovementsTable);
