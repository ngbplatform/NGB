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

public interface IDocumentCombinedPageReader : IDocumentReader
{
    Task<DocumentHeadQueryPage> GetPageWithTotalAsync(
        DocumentHeadDescriptor head,
        DocumentQuery query,
        int offset,
        int limit,
        CancellationToken ct = default);
}

public interface IDocumentSeekPageReader : IDocumentCombinedPageReader
{
    Task<DocumentHeadSeekPage> GetSeekPageAsync(
        DocumentHeadDescriptor head,
        DocumentQuery query,
        string? afterDisplay,
        Guid? afterId,
        int limit,
        bool includeTotal,
        CancellationToken ct = default);
}

public sealed record DocumentHeadQueryPage(IReadOnlyList<DocumentHeadRow> Rows, long Total);

public sealed record DocumentHeadSeekPage(
    IReadOnlyList<DocumentHeadRow> Rows,
    long? Total,
    bool HasMore,
    string? NextAfterDisplay,
    Guid? NextAfterId);
