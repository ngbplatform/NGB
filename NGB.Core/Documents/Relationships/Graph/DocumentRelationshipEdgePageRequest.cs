namespace NGB.Core.Documents.Relationships.Graph;

/// <summary>
/// Keyset paging request for listing relationship edges around a single document.
/// </summary>
public sealed record DocumentRelationshipEdgePageRequest(
    Guid DocumentId,
    string? RelationshipCode = null,
    int PageSize = 100,
    DocumentRelationshipEdgeCursor? Cursor = null);
