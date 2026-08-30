using System.Collections.Concurrent;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.PostgreSql.ReferenceRegisters;

/// <summary>
/// Caches reference-register metadata only after records make that metadata immutable.
/// Mutable and missing registers remain immediately visible to readers and writers.
/// </summary>
public sealed class ReferenceRegisterMetadataCache(TimeProvider timeProvider)
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<Guid, CacheEntry> _entries = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _loadGates = new();

    public async Task<ReferenceRegisterMetadataContext> GetOrCreateAsync(
        Guid registerId,
        Func<CancellationToken, Task<ReferenceRegisterMetadataContext>> factory,
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

    public void Remember(ReferenceRegisterMetadataContext context)
    {
        if (context.Register.HasRecords)
            _entries[context.Register.RegisterId] = new CacheEntry(context, timeProvider.GetUtcNow().Add(TimeToLive));
    }

    public void Invalidate(Guid registerId) => _entries.TryRemove(registerId, out _);

    private bool TryGet(Guid registerId, out ReferenceRegisterMetadataContext context)
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

    private sealed record CacheEntry(ReferenceRegisterMetadataContext Context, DateTimeOffset ExpiresAtUtc);
}

public sealed record ReferenceRegisterMetadataContext(
    ReferenceRegisterAdminItem Register,
    IReadOnlyList<ReferenceRegisterField> Fields,
    string RecordsTable);
