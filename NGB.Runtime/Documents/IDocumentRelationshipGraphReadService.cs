using NGB.Core.Documents.Relationships.Graph;

namespace NGB.Runtime.Documents;

/// <summary>
/// Runtime-level read facade for Document Relationships (UI navigation / graph).
///
/// Consumers should prefer this abstraction over directly depending on persistence readers.
/// It provides a stable API surface and may apply runtime defaults/validation.
/// </summary>
public interface IDocumentRelationshipGraphReadService
{
    Task<DocumentRelationshipEdgePage> GetOutgoingPageAsync(
        DocumentRelationshipEdgePageRequest request,
        CancellationToken ct = default);

    Task<DocumentRelationshipEdgePage> GetIncomingPageAsync(
        DocumentRelationshipEdgePageRequest request,
        CancellationToken ct = default);

    Task<DocumentRelationshipGraph> GetGraphAsync(
        DocumentRelationshipGraphRequest request,
        CancellationToken ct = default);
}
