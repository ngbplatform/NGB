using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace NGB.Runtime.Security;

public sealed class NgbSecurityCache(IMemoryCache cache, IOptionsMonitor<NgbSecurityCacheOptions> options)
{
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

        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            return await factory(ct);
        });
    }

    private static string NormalizeCachePart(string value) => value.Trim().ToLowerInvariant();
}
