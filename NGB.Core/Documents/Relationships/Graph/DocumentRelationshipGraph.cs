namespace NGB.Core.Documents.Relationships.Graph;

public sealed record DocumentRelationshipGraph(
    Guid RootDocumentId,
    IReadOnlyList<DocumentRelationshipGraphNode> Nodes,
    IReadOnlyList<DocumentRelationshipGraphEdge> Edges);
