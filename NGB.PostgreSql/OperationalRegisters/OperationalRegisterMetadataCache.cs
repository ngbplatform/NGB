using NGB.OperationalRegisters.Contracts;
using NGB.PostgreSql.Internal;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Caches operational-register metadata only after the first movement makes that metadata
/// immutable at the database level. Mutable and missing registers are deliberately not cached.
/// </summary>
public sealed class OperationalRegisterMetadataCache
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);
    private const int DefaultCapacity = 4_096;
    private readonly TimeProvider _timeProvider;
    private readonly BoundedExpiringCache<Guid, OperationalRegisterMetadataContext> _entries;
    private readonly AsyncKeyedLock<Guid> _loadGates = new();

    public OperationalRegisterMetadataCache(TimeProvider timeProvider)
        : this(timeProvider, DefaultCapacity)
    {
    }

    internal OperationalRegisterMetadataCache(TimeProvider timeProvider, int capacity)
    {
        _timeProvider = timeProvider;
        _entries = new BoundedExpiringCache<Guid, OperationalRegisterMetadataContext>(capacity);
    }

    internal int EntryCount => _entries.Count;
    internal int LoadGateCount => _loadGates.Count;

    public async Task<OperationalRegisterMetadataContext> GetOrCreateAsync(
        Guid registerId,
        Func<CancellationToken, Task<OperationalRegisterMetadataContext>> factory,
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

    public void Remember(OperationalRegisterMetadataContext context)
    {
        if (context.Register.HasMovements)
        {
            var now = _timeProvider.GetUtcNow();
            _entries.Set(context.Register.RegisterId, context, now.Add(TimeToLive), now);
        }
    }

    public void Invalidate(Guid registerId) => _entries.Remove(registerId);

    private bool TryGet(Guid registerId, out OperationalRegisterMetadataContext context)
    {
        return _entries.TryGet(registerId, _timeProvider.GetUtcNow(), out context);
    }
}

public sealed record OperationalRegisterMetadataContext(
    OperationalRegisterAdminItem Register,
    OperationalRegisterResource[] Resources,
    string MovementsTable);
