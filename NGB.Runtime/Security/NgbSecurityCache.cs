using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace NGB.Runtime.Security;

public sealed class NgbSecurityCache(IMemoryCache cache, IOptionsMonitor<NgbSecurityCacheOptions> options)
{
    private readonly ConcurrentDictionary<string, byte> trackedKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> pendingPopulations = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> insertionOrder = new();
    private int trackedKeyCount;

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

        var population = pendingPopulations.GetOrAdd(
            key,
            _ => new Lazy<Task<object?>>(
                async () =>
                {
                    var created = await factory(ct);
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
                },
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var value = await population.Value.WaitAsync(ct);
            return (T?)value;
        }
        finally
        {
            if (population.IsValueCreated && population.Value.IsCompleted)
            {
                pendingPopulations.TryRemove(new KeyValuePair<string, Lazy<Task<object?>>>(key, population));
            }
        }
    }

    private void TrackAndTrim(string key, int maxEntries)
    {
        if (trackedKeys.TryAdd(key, 0))
        {
            Interlocked.Increment(ref trackedKeyCount);
            insertionOrder.Enqueue(key);
        }

        while (Volatile.Read(ref trackedKeyCount) > maxEntries && insertionOrder.TryDequeue(out var oldest))
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

        Interlocked.Decrement(ref trackedKeyCount);
        return true;
    }

    private static string NormalizeCachePart(string value) => value.Trim().ToLowerInvariant();
}
