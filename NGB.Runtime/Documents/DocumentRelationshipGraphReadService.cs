using NGB.Core.Documents.Relationships.Graph;
using NGB.Persistence.Readers.Documents;

namespace NGB.Runtime.Documents;

/// <summary>
/// Runtime-level wrapper over <see cref="IDocumentRelationshipGraphReader"/>.
/// Applies safe defaults and guards to keep UI callers resilient.
/// </summary>
internal sealed class DocumentRelationshipGraphReadService(IDocumentRelationshipGraphReader reader)
    : IDocumentRelationshipGraphReadService
{
    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 500;

    public Task<DocumentRelationshipEdgePage> GetOutgoingPageAsync(
        DocumentRelationshipEdgePageRequest request,
        CancellationToken ct = default)
        => reader.GetOutgoingPageAsync(NormalizePageRequest(request), ct);

    public Task<DocumentRelationshipEdgePage> GetIncomingPageAsync(
        DocumentRelationshipEdgePageRequest request,
        CancellationToken ct = default)
        => reader.GetIncomingPageAsync(NormalizePageRequest(request), ct);

    public Task<DocumentRelationshipGraph> GetGraphAsync(
        DocumentRelationshipGraphRequest request,
        CancellationToken ct = default)
        => reader.GetGraphAsync(request, ct);

    private static DocumentRelationshipEdgePageRequest NormalizePageRequest(DocumentRelationshipEdgePageRequest request)
    {
        var size = request.PageSize;

        if (size <= 0)
            size = DefaultPageSize;

        if (size > MaxPageSize)
            size = MaxPageSize;

        return size == request.PageSize ? request : request with { PageSize = size };
    }
}
