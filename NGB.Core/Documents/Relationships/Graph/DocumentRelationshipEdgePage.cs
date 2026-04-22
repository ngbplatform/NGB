namespace NGB.Core.Documents.Relationships.Graph;

public sealed record DocumentRelationshipEdgePage(
    IReadOnlyList<DocumentRelationshipEdgeItem> Items,
    bool HasMore,
    DocumentRelationshipEdgeCursor? NextCursor);
