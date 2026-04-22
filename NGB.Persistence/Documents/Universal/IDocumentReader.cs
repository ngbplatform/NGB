namespace NGB.Persistence.Documents.Universal;

public interface IDocumentReader
{
    Task<long> CountAsync(DocumentHeadDescriptor head, DocumentQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentHeadRow>> GetPageAsync(
        DocumentHeadDescriptor head,
        DocumentQuery query,
        int offset,
        int limit,
        CancellationToken ct = default);

    Task<DocumentHeadRow?> GetByIdAsync(DocumentHeadDescriptor head, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentHeadRow>> GetByIdsAsync(
        DocumentHeadDescriptor head,
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default);

    Task<IReadOnlyList<DocumentHeadRow>> GetHeadRowsByIdsAcrossTypesAsync(
        IReadOnlyList<DocumentHeadDescriptor> heads,
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default);

    Task<IReadOnlyList<DocumentLookupRow>> LookupAcrossTypesAsync(
        IReadOnlyList<DocumentHeadDescriptor> heads,
        string? query,
        int perTypeLimit,
        bool activeOnly,
        CancellationToken ct = default);

    Task<IReadOnlyList<DocumentLookupRow>> GetByIdsAcrossTypesAsync(
        IReadOnlyList<DocumentHeadDescriptor> heads,
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default);
}
