using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace NGB.Runtime.Security;

public sealed class NgbSecurityCache(IMemoryCache cache, IOptionsMonitor<NgbSecurityCacheOptions> options)
{
    private readonly ConcurrentDictionary<string, byte> trackedKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingPopulation> pendingPopulations = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _insertionOrder = new();
    private int _trackedKeyCount;

    public Task<T?> GetOrCreatePermissionSnapshotAsync<T>(
        Guid userId,
        long accessVersion,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
        => GetOrCreateAsync(
            $"ngb:security:snapshot:{userId:N}:{Math.Max(accessVersion, 0)}",
            options.CurrentValue.PermissionSnapshotTtl,
            factory,
            ct);

    public Task<T?> GetOrCreateMainMenuAsync<T>(
        PermissionSnapshot snapshot,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
        => GetOrCreateAsync(
            $"ngb:security:main-menu:{snapshot.AccessCacheKey}",
            options.CurrentValue.MainMenuTtl,
            factory,
            ct);

    public Task<T?> GetOrCreateCatalogMetadataAsync<T>(
        PermissionSnapshot snapshot,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
        => GetOrCreateAsync(
            $"ngb:security:catalog-metadata:all:{snapshot.AccessCacheKey}",
            options.CurrentValue.CatalogMetadataTtl,
            factory,
            ct);

    public Task<T?> GetOrCreateCatalogTypeMetadataAsync<T>(
        PermissionSnapshot snapshot,
        string catalogType,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
        => GetOrCreateAsync(
            $"ngb:security:catalog-metadata:type:{snapshot.AccessCacheKey}:{NormalizeCachePart(catalogType)}",
            options.CurrentValue.CatalogMetadataTtl,
            factory,
            ct);

    public Task<T?> GetOrCreateDocumentMetadataAsync<T>(
        PermissionSnapshot snapshot,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
        => GetOrCreateAsync(
            $"ngb:security:document-metadata:all:{snapshot.AccessCacheKey}",
            options.CurrentValue.DocumentMetadataTtl,
            factory,
            ct);

    public Task<T?> GetOrCreateDocumentTypeMetadataAsync<T>(
        PermissionSnapshot snapshot,
        string documentType,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
        => GetOrCreateAsync(
            $"ngb:security:document-metadata:type:{snapshot.AccessCacheKey}:{NormalizeCachePart(documentType)}",
            options.CurrentValue.DocumentMetadataTtl,
            factory,
            ct);

    public Task<T?> GetOrCreateReportDefinitionsAsync<T>(
        PermissionSnapshot snapshot,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
        => GetOrCreateAsync(
            $"ngb:security:report-definitions:{snapshot.AccessCacheKey}",
            options.CurrentValue.ReportDefinitionsTtl,
            factory,
            ct);

    private async Task<T?> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (cache.TryGetValue<T>(key, out var cached))
            return cached;

        while (true)
        {
            var population = pendingPopulations.GetOrAdd(
                key,
                _ => new PendingPopulation(async populationCt =>
                {
                    var created = await factory(populationCt);
                    TrackAndTrim(key, options.CurrentValue.MaxEntries);
                    cache.Set(
                        key,
                        created,
                        new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = ttl,
                            Size = 1
                        }.RegisterPostEvictionCallback(static (evictedKey, _, _, state) =>
                            {
                                if (evictedKey is string stringKey && state is NgbSecurityCache securityCache)
                                    securityCache.TryRemoveTracked(stringKey);
                            },
                            this));

                    return created;
                }));

            if (!population.TryAddWaiter())
            {
                pendingPopulations.TryRemove(new KeyValuePair<string, PendingPopulation>(key, population));
                continue;
            }

            var task = population.Task;
            _ = task.ContinueWith(
                static (_, state) =>
                {
                    var cleanup = ((NgbSecurityCache Cache, string Key, PendingPopulation Population))state!;
                    cleanup.Cache.pendingPopulations.TryRemove(
                        new KeyValuePair<string, PendingPopulation>(cleanup.Key, cleanup.Population));
                    cleanup.Population.Dispose();
                },
                (this, key, population),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            try
            {
                var value = await task.WaitAsync(ct);
                return (T?)value;
            }
            finally
            {
                if (population.ReleaseWaiterAndAbandonIfLast())
                {
                    pendingPopulations.TryRemove(new KeyValuePair<string, PendingPopulation>(key, population));
                    population.Cancel();
                }
            }
        }
    }

    private void TrackAndTrim(string key, int maxEntries)
    {
        if (trackedKeys.TryAdd(key, 0))
        {
            Interlocked.Increment(ref _trackedKeyCount);
            _insertionOrder.Enqueue(key);
        }

        while (Volatile.Read(ref _trackedKeyCount) > maxEntries && _insertionOrder.TryDequeue(out var oldest))
        {
            if (!TryRemoveTracked(oldest))
                continue;

            cache.Remove(oldest);
        }
    }

    private bool TryRemoveTracked(string key)
    {
        if (!trackedKeys.TryRemove(key, out _))
            return false;

        Interlocked.Decrement(ref _trackedKeyCount);
        return true;
    }

    private static string NormalizeCachePart(string value) => value.Trim().ToLowerInvariant();

    private sealed class PendingPopulation : IDisposable
    {
        private readonly Lock _sync = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Lazy<Task<object?>> _task;
        private int _waiters;
        private bool _abandoned;

        public PendingPopulation(Func<CancellationToken, Task<object?>> factory)
        {
            _task = new Lazy<Task<object?>>(
                () => factory(_cts.Token),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public Task<object?> Task => _task.Value;

        public bool TryAddWaiter()
        {
            lock (_sync)
            {
                if (_abandoned)
                    return false;

                _waiters++;
                return true;
            }
        }

        public bool ReleaseWaiterAndAbandonIfLast()
        {
            lock (_sync)
            {
                _waiters--;
                if (_waiters != 0 || (_task.IsValueCreated && _task.Value.IsCompleted))
                    return false;

                _abandoned = true;
                return true;
            }
        }

        public void Cancel()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Completion won the race and already disposed the population token source.
            }
        }

        public void Dispose() => _cts.Dispose();
    }
}
