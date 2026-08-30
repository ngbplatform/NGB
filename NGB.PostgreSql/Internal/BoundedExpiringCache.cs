using System.Collections.Concurrent;

namespace NGB.PostgreSql.Internal;

/// <summary>
/// Small bounded TTL cache for immutable database metadata. Eviction is FIFO by insertion
/// generation; expired entries are removed eagerly on reads and writes.
/// </summary>
internal sealed class BoundedExpiringCache<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Entry> _entries;
    private readonly Lock _writeLock = new();
    private readonly int _capacity;
    private long _generation;

    public BoundedExpiringCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _capacity = capacity;
        _entries = new ConcurrentDictionary<TKey, Entry>(comparer ?? EqualityComparer<TKey>.Default);
    }

    internal int Count => _entries.Count;

    public bool TryGet(TKey key, DateTimeOffset now, out TValue value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAtUtc > now)
            {
                value = entry.Value;
                return true;
            }

            _entries.TryRemove(new KeyValuePair<TKey, Entry>(key, entry));
        }

        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value, DateTimeOffset expiresAtUtc, DateTimeOffset now)
    {
        lock (_writeLock)
        {
            foreach (var candidate in _entries)
            {
                if (candidate.Value.ExpiresAtUtc <= now)
                    _entries.TryRemove(candidate);
            }

            _entries[key] = new Entry(value, expiresAtUtc, Interlocked.Increment(ref _generation));
            while (_entries.Count > _capacity)
            {
                var oldest = _entries.MinBy(static candidate => candidate.Value.Generation);
                if (!_entries.TryRemove(oldest))
                    break;
            }
        }
    }

    public void Remove(TKey key) => _entries.TryRemove(key, out _);

    public void RemoveWhere(Func<TKey, bool> predicate)
    {
        foreach (var key in _entries.Keys)
        {
            if (predicate(key))
                _entries.TryRemove(key, out _);
        }
    }

    private sealed record Entry(TValue Value, DateTimeOffset ExpiresAtUtc, long Generation);
}
