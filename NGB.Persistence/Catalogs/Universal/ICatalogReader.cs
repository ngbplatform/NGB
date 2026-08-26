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

    Task<IReadOnlyList<CatalogHeadRow>> GetByIdsWithFieldsAsync(
        CatalogHeadDescriptor head,
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetActiveDescendantIdsAsync(
        CatalogHeadDescriptor head,
        IReadOnlyList<Guid> rootIds,
        string parentColumnCode,
        CancellationToken ct = default);

    Task<bool> HasParentChainViolationAsync(
        CatalogHeadDescriptor head,
        Guid catalogId,
        Guid parentId,
        string parentColumnCode,
        int maxDepth,
        CancellationToken ct = default);

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

public interface ICatalogCombinedPageReader : ICatalogReader
{
    Task<CatalogHeadQueryPage> GetPageWithTotalAsync(
        CatalogHeadDescriptor head,
        CatalogQuery query,
        int offset,
        int limit,
        CancellationToken ct = default);
}

public sealed record CatalogHeadQueryPage(
    IReadOnlyList<CatalogHeadRow> Rows,
    long Total);
