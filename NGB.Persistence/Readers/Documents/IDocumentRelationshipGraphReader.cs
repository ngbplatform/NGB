using NGB.Core.Documents.Relationships.Graph;

namespace NGB.Persistence.Readers.Documents;

/// <summary>
/// Read model for Document Relationships targeted for UI navigation and graph visualizations.
/// </summary>
public interface IDocumentRelationshipGraphReader
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
