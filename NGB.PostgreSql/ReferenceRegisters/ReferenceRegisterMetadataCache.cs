using NGB.PostgreSql.Internal;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.PostgreSql.ReferenceRegisters;

/// <summary>
/// Caches reference-register metadata only after records make that metadata immutable.
/// Mutable and missing registers remain immediately visible to readers and writers.
/// </summary>
public sealed class ReferenceRegisterMetadataCache
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);
    private const int DefaultCapacity = 4_096;
    private readonly TimeProvider _timeProvider;
    private readonly BoundedExpiringCache<Guid, ReferenceRegisterMetadataContext> _entries;
    private readonly AsyncKeyedLock<Guid> _loadGates = new();

    public ReferenceRegisterMetadataCache(TimeProvider timeProvider)
        : this(timeProvider, DefaultCapacity)
    {
    }

    internal ReferenceRegisterMetadataCache(TimeProvider timeProvider, int capacity)
    {
        _timeProvider = timeProvider;
        _entries = new BoundedExpiringCache<Guid, ReferenceRegisterMetadataContext>(capacity);
    }

    internal int EntryCount => _entries.Count;
    internal int LoadGateCount => _loadGates.Count;

    public async Task<ReferenceRegisterMetadataContext> GetOrCreateAsync(
        Guid registerId,
        Func<CancellationToken, Task<ReferenceRegisterMetadataContext>> factory,
        CancellationToken ct)
    {
        if (TryGet(registerId, out var cached))
            return cached;

        using (await _loadGates.AcquireAsync(registerId, ct))
        {
            if (TryGet(registerId, out cached))
                return cached;

            var created = await factory(ct);
            Remember(created);

            return created;
        }
    }

    public void Remember(ReferenceRegisterMetadataContext context)
    {
        if (context.Register.HasRecords)
        {
            var now = _timeProvider.GetUtcNow();
            _entries.Set(context.Register.RegisterId, context, now.Add(TimeToLive), now);
        }
    }

    public void Invalidate(Guid registerId) => _entries.Remove(registerId);

    private bool TryGet(Guid registerId, out ReferenceRegisterMetadataContext context)
    {
        return _entries.TryGet(registerId, _timeProvider.GetUtcNow(), out context);
    }
}

public sealed record ReferenceRegisterMetadataContext(
    ReferenceRegisterAdminItem Register,
    IReadOnlyList<ReferenceRegisterField> Fields,
    string RecordsTable);
