using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.Security;
using NGB.Runtime.Security;

namespace NGB.Runtime.Catalogs;

public sealed class PermissionAwareCatalogService(CatalogService inner, INgbAccessChecker access) : ICatalogService
{
    public async Task<IReadOnlyList<CatalogTypeMetadataDto>> GetAllMetadataAsync(CancellationToken ct)
    {
        var metadata = await inner.GetAllMetadataAsync(ct);
        var result = new List<CatalogTypeMetadataDto>(metadata.Count);
        var snapshot = await access.GetSnapshotAsync(ct);

        foreach (var item in metadata)
        {
            if (Has(snapshot, item.CatalogType, NgbPermissionActions.View))
                result.Add(ApplyCapabilities(item, snapshot));
        }

        return result;
    }

    public async Task<CatalogTypeMetadataDto> GetTypeMetadataAsync(string catalogType, CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        Require(snapshot, catalogType, NgbPermissionActions.View);
        var metadata = await inner.GetTypeMetadataAsync(catalogType, ct);
        return ApplyCapabilities(metadata, snapshot);
    }

    public async Task<PageResponseDto<CatalogItemDto>> GetPageAsync(
        string catalogType,
        PageRequestDto request,
        CancellationToken ct)
    {
        await RequireAsync(catalogType, NgbPermissionActions.View, ct);
        return await inner.GetPageAsync(catalogType, request, ct);
    }

    public async Task<CatalogItemDto> GetByIdAsync(string catalogType, Guid id, CancellationToken ct)
    {
        await RequireAsync(catalogType, NgbPermissionActions.View, ct);
        return await inner.GetByIdAsync(catalogType, id, ct);
    }

    public async Task<IReadOnlyList<CatalogLookupDto>> LookupAcrossTypesAsync(
        IReadOnlyList<string> catalogTypes,
        string? query,
        int perTypeLimit,
        bool activeOnly,
        CancellationToken ct)
    {
        var allowed = await FilterAsync(catalogTypes, NgbPermissionActions.Lookup, ct);
        return allowed.Count == 0
            ? []
            : await inner.LookupAcrossTypesAsync(allowed, query, perTypeLimit, activeOnly, ct);
    }

    public async Task<CatalogItemDto> CreateAsync(string catalogType, RecordPayload payload, CancellationToken ct)
    {
        await RequireAsync(catalogType, NgbPermissionActions.Create, ct);
        return await inner.CreateAsync(catalogType, payload, ct);
    }

    public async Task<CatalogItemDto> UpdateAsync(
        string catalogType,
        Guid id,
        RecordPayload payload,
        CancellationToken ct)
    {
        await RequireAsync(catalogType, NgbPermissionActions.Edit, ct);
        return await inner.UpdateAsync(catalogType, id, payload, ct);
    }

    public async Task MarkForDeletionAsync(string catalogType, Guid id, CancellationToken ct)
    {
        await RequireAsync(catalogType, NgbPermissionActions.MarkForDeletion, ct);
        await inner.MarkForDeletionAsync(catalogType, id, ct);
    }

    public async Task UnmarkForDeletionAsync(string catalogType, Guid id, CancellationToken ct)
    {
        await RequireAsync(catalogType, NgbPermissionActions.UnmarkForDeletion, ct);
        await inner.UnmarkForDeletionAsync(catalogType, id, ct);
    }

    public async Task<IReadOnlyList<LookupItemDto>> LookupAsync(
        string catalogType,
        string? query,
        int limit,
        CancellationToken ct)
    {
        await RequireAsync(catalogType, NgbPermissionActions.Lookup, ct);
        return await inner.LookupAsync(catalogType, query, limit, ct);
    }

    public async Task<IReadOnlyList<LookupItemDto>> GetByIdsAsync(
        string catalogType,
        IReadOnlyList<Guid> ids,
        CancellationToken ct)
    {
        await RequireAsync(catalogType, NgbPermissionActions.Lookup, ct);
        return await inner.GetByIdsAsync(catalogType, ids, ct);
    }

    private Task RequireAsync(string catalogType, string action, CancellationToken ct)
        => access.RequireAsync(NgbResourceKinds.Catalog, catalogType, action, ct);

    private static CatalogTypeMetadataDto ApplyCapabilities(
        CatalogTypeMetadataDto metadata,
        PermissionSnapshot snapshot)
    {
        var catalogType = metadata.CatalogType;
        var current = metadata.Capabilities ?? new CatalogCapabilitiesDto();

        return metadata with
        {
            Capabilities = current with
            {
                CanCreate = current.CanCreate && Has(snapshot, catalogType, NgbPermissionActions.Create),
                CanEdit = current.CanEdit && Has(snapshot, catalogType, NgbPermissionActions.Edit),
                CanDelete = false,
                CanMarkForDeletion = current.CanMarkForDeletion && Has(snapshot, catalogType, NgbPermissionActions.MarkForDeletion)
            }
        };
    }

    private async Task<IReadOnlyList<string>> FilterAsync(
        IReadOnlyList<string> catalogTypes,
        string action,
        CancellationToken ct)
    {
        if (catalogTypes is null || catalogTypes.Count == 0)
            return [];

        var snapshot = await access.GetSnapshotAsync(ct);
        var result = new List<string>(catalogTypes.Count);
        foreach (var catalogType in catalogTypes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Has(snapshot, catalogType, action))
                result.Add(catalogType);
        }

        return result;
    }

    private static void Require(PermissionSnapshot snapshot, string catalogType, string action)
    {
        var permission = new NgbPermissionKey(NgbResourceKinds.Catalog, catalogType, action);
        if (!snapshot.Has(permission))
            throw new NgbPermissionDeniedException(permission);
    }

    private static bool Has(PermissionSnapshot snapshot, string catalogType, string action)
        => snapshot.Has(NgbResourceKinds.Catalog, catalogType, action);
}
