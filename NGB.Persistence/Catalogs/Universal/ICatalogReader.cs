namespace NGB.Persistence.Catalogs.Universal;

/// <summary>
/// Provider-specific reader for universal, metadata-driven catalog CRUD.
/// </summary>
public interface ICatalogReader
{
    Task<long> CountAsync(CatalogHeadDescriptor head, CatalogQuery query, CancellationToken ct = default);
    
    Task<IReadOnlyList<CatalogHeadRow>> GetPageAsync(
        CatalogHeadDescriptor head,
        CatalogQuery query,
        int offset,
        int limit,
        CancellationToken ct = default);
    
    Task<CatalogHeadRow?> GetByIdAsync(CatalogHeadDescriptor head, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<CatalogLookupRow>> LookupAsync(
        CatalogHeadDescriptor head,
        string? query,
        int limit,
        CancellationToken ct = default);
    
    Task<IReadOnlyList<CatalogLookupRow>> GetByIdsAsync(
        CatalogHeadDescriptor head,
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default);

    Task<IReadOnlyList<CatalogLookupSearchRow>> LookupAcrossTypesAsync(
        IReadOnlyList<CatalogHeadDescriptor> heads,
        string? query,
        int perTypeLimit,
        bool activeOnly,
        CancellationToken ct = default);
}
