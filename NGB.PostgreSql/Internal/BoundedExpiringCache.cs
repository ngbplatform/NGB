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
    private readonly LinkedList<TKey> _insertionOrder = [];
    private readonly Dictionary<TKey, LinkedListNode<TKey>> _orderNodes;
    private readonly PriorityQueue<ExpirationToken, long> _expirations = new();
    private readonly int _capacity;
    private long _generation;

    public BoundedExpiringCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _capacity = capacity;
        var effectiveComparer = comparer ?? EqualityComparer<TKey>.Default;
        _entries = new ConcurrentDictionary<TKey, Entry>(effectiveComparer);
        _orderNodes = new Dictionary<TKey, LinkedListNode<TKey>>(effectiveComparer);
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

            lock (_writeLock)
            {
                RemoveLocked(key, entry.Generation);
            }
        }

        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value, DateTimeOffset expiresAtUtc, DateTimeOffset now)
    {
        lock (_writeLock)
        {
            PurgeExpiredLocked(now);

            var generation = Interlocked.Increment(ref _generation);
            if (_orderNodes.Remove(key, out var existingNode))
                _insertionOrder.Remove(existingNode);

            _entries[key] = new Entry(value, expiresAtUtc, generation);
            _orderNodes[key] = _insertionOrder.AddLast(key);
            _expirations.Enqueue(new ExpirationToken(key, generation), expiresAtUtc.UtcDateTime.Ticks);

            while (_entries.Count > _capacity)
            {
                RemoveLocked(_insertionOrder.First!.Value);
            }

            CompactExpirationQueueIfNeededLocked();
        }
    }

    public void Remove(TKey key)
    {
        lock (_writeLock)
        {
            RemoveLocked(key);
        }
    }

    public void RemoveWhere(Func<TKey, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (_writeLock)
        {
            foreach (var key in _entries.Keys)
            {
                if (predicate(key))
                    RemoveLocked(key);
            }

            CompactExpirationQueueIfNeededLocked();
        }
    }

    private void PurgeExpiredLocked(DateTimeOffset now)
    {
        var nowTicks = now.UtcDateTime.Ticks;
        while (_expirations.TryPeek(out var token, out var expiresAtTicks) && expiresAtTicks <= nowTicks)
        {
            _expirations.Dequeue();
            RemoveLocked(token.Key, token.Generation);
        }
    }

    private void RemoveLocked(TKey key, long? expectedGeneration = null)
    {
        if (!_entries.TryGetValue(key, out var entry)
            || (expectedGeneration.HasValue && entry.Generation != expectedGeneration.Value)
            || !_entries.TryRemove(new KeyValuePair<TKey, Entry>(key, entry)))
        {
            return;
        }

        if (_orderNodes.Remove(key, out var node))
            _insertionOrder.Remove(node);
    }

    private void CompactExpirationQueueIfNeededLocked()
    {
        var threshold = Math.Max(16L, (long)_entries.Count * 4);
        if (_expirations.Count <= threshold)
            return;

        _expirations.Clear();
        foreach (var (key, entry) in _entries)
        {
            _expirations.Enqueue(new ExpirationToken(key, entry.Generation), entry.ExpiresAtUtc.UtcDateTime.Ticks);
        }
    }

    private sealed record Entry(TValue Value, DateTimeOffset ExpiresAtUtc, long Generation);

    private readonly record struct ExpirationToken(TKey Key, long Generation);
}
