using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace NGB.Hosting.AspNetCore.Identity;

public sealed class MemoryCacheTicketStore : ITicketStore, IDisposable
{
    private const string KeyPrefix = "AuthSessionStore-";
    private const int DefaultMaximumSessionCount = 10_000;

    private readonly MemoryCache _cache;
    private readonly object _writeGate = new();
    private readonly int _maximumSessionCount;
    private readonly ConcurrentDictionary<string, long> _versions = new(StringComparer.Ordinal);
    private ConcurrentQueue<AccessStamp> _recency = new();
    private long _nextVersion;
    private int _recencyStampCount;

    internal int TrackedSessionCount => _versions.Count;

    internal int RecencyMetadataCount => Volatile.Read(ref _recencyStampCount);

    public MemoryCacheTicketStore() : this(DefaultMaximumSessionCount)
    {
    }

    public MemoryCacheTicketStore(int maximumSessionCount)
    {
        if (maximumSessionCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSessionCount));

        _maximumSessionCount = maximumSessionCount;
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = maximumSessionCount
        });
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var guid = Guid.CreateVersion7();
        var key = KeyPrefix + guid;
        await RenewAsync(key, ticket);
        
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var options = new MemoryCacheEntryOptions();
        
        var expiresUtc = ticket.Properties.ExpiresUtc;
        if (expiresUtc.HasValue)
            options.SetAbsoluteExpiration(expiresUtc.Value);
        
        options.SetSlidingExpiration(TimeSpan.FromHours(1));
        options.SetSize(1);

        lock (_writeGate)
        {
            if (!_versions.ContainsKey(key))
            {
                while (_versions.Count >= _maximumSessionCount)
                {
                    RemoveLeastRecentlyUsedLocked();
                }
            }

            _cache.Set(key, ticket, options);
            RecordNewGeneration(key);
        }

        CompactRecencyMetadataIfNeeded();
        
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        _versions.TryGetValue(key, out var observedVersion);
        if (!_cache.TryGetValue(key, out AuthenticationTicket? ticket))
        {
            if (observedVersion != 0)
                _versions.TryRemove(new KeyValuePair<string, long>(key, observedVersion));

            return Task.FromResult<AuthenticationTicket?>(null);
        }

        Touch(key);
        return Task.FromResult(ticket);
    }

    public Task RemoveAsync(string key)
    {
        lock (_writeGate)
        {
            _cache.Remove(key);
            _versions.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    private void RemoveLeastRecentlyUsedLocked()
    {
        while (_recency.TryDequeue(out var oldest))
        {
            Interlocked.Decrement(ref _recencyStampCount);
            if (!_versions.TryGetValue(oldest.Key, out var currentVersion)
                || currentVersion != oldest.Version
                || !_versions.TryRemove(new KeyValuePair<string, long>(oldest.Key, currentVersion)))
            {
                continue;
            }

            _cache.Remove(oldest.Key);
            return;
        }

        // A concurrent compaction may have dropped a racing access stamp. Capacity must
        // still be enforced, so fall back to any tracked entry in this extremely rare race.
        TryRemoveFallback(_versions, fallback => _versions.TryRemove(fallback), _cache.Remove);
    }

    internal static bool TryRemoveFallback(
        IEnumerable<KeyValuePair<string, long>> candidates,
        Func<KeyValuePair<string, long>, bool> tryRemove,
        Action<string> removeCacheEntry)
    {
        if (candidates.FirstOrDefault() is not { Key.Length: > 0 } fallback)
            return false;

        if (!tryRemove(fallback))
            return false;

        removeCacheEntry(fallback.Key);
        return true;
    }

    private void RecordNewGeneration(string key)
    {
        var version = Interlocked.Increment(ref _nextVersion);
        _versions[key] = version;
        EnqueueAccess(new AccessStamp(key, version));
    }

    private void Touch(string key)
    {
        while (_versions.TryGetValue(key, out var currentVersion))
        {
            var nextVersion = Interlocked.Increment(ref _nextVersion);
            if (!_versions.TryUpdate(key, nextVersion, currentVersion))
                continue;

            EnqueueAccess(new AccessStamp(key, nextVersion));
            CompactRecencyMetadataIfNeeded();
            return;
        }
    }

    private void EnqueueAccess(AccessStamp stamp)
    {
        Volatile.Read(ref _recency).Enqueue(stamp);
        Interlocked.Increment(ref _recencyStampCount);
    }

    private void CompactRecencyMetadataIfNeeded()
    {
        var metadataLimit = (int)Math.Min(Math.Max((long)_maximumSessionCount * 4, 128L), int.MaxValue);

        if (Volatile.Read(ref _recencyStampCount) <= metadataLimit || !Monitor.TryEnter(_writeGate))
            return;

        try
        {
            var compacted = new ConcurrentQueue<AccessStamp>(_versions.Select(static pair => new AccessStamp(pair.Key, pair.Value)));
            Volatile.Write(ref _recency, compacted);
            Volatile.Write(ref _recencyStampCount, compacted.Count);
        }
        finally
        {
            Monitor.Exit(_writeGate);
        }
    }

    private readonly record struct AccessStamp(string Key, long Version);

    public void Dispose() => _cache.Dispose();
}
