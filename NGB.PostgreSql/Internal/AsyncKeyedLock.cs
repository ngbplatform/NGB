using System.Collections.Concurrent;

namespace NGB.PostgreSql.Internal;

/// <summary>
/// Coalesces work per key without retaining one semaphore for every key ever observed.
/// Entries are retired only after the owner and all queued/cancelled waiters release them.
/// </summary>
internal sealed class AsyncKeyedLock<TKey>(IEqualityComparer<TKey>? comparer = null)
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Entry> _entries = new(comparer ?? EqualityComparer<TKey>.Default);

    internal int Count => _entries.Count;

    public async ValueTask<IDisposable> AcquireAsync(TKey key, CancellationToken ct)
    {
        while (true)
        {
            var entry = _entries.GetOrAdd(key, static _ => new Entry());
            if (!entry.TryRent())
            {
                _entries.TryRemove(new KeyValuePair<TKey, Entry>(key, entry));
                continue;
            }

            try
            {
                await entry.Semaphore.WaitAsync(ct);
                return new Lease(this, key, entry);
            }
            catch
            {
                Return(key, entry, releaseSemaphore: false);
                throw;
            }
        }
    }

    private void Return(TKey key, Entry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
            entry.Semaphore.Release();

        if (!entry.ReturnAndTryRetire())
            return;

        _entries.TryRemove(new KeyValuePair<TKey, Entry>(key, entry));
        entry.Semaphore.Dispose();
    }

    private sealed class Entry
    {
        private int _rentCount;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryRent()
        {
            while (true)
            {
                var observed = Volatile.Read(ref _rentCount);
                if (observed < 0)
                    return false;

                if (Interlocked.CompareExchange(ref _rentCount, observed + 1, observed) == observed)
                    return true;
            }
        }

        public bool ReturnAndTryRetire()
        {
            if (Interlocked.Decrement(ref _rentCount) != 0)
                return false;

            return Interlocked.CompareExchange(ref _rentCount, -1, 0) == 0;
        }
    }

    private sealed class Lease(AsyncKeyedLock<TKey> owner, TKey key, Entry entry) : IDisposable
    {
        private AsyncKeyedLock<TKey>? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Return(key, entry, releaseSemaphore: true);
    }
}
