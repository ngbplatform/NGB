namespace NGB.Core.Documents.Relationships.Graph;

/// <summary>
/// Request for building a bounded neighborhood graph around a document.
/// The reader performs a BFS up to <see cref="MaxDepth"/>.
/// </summary>
public sealed record DocumentRelationshipGraphRequest(
    Guid RootDocumentId,
    int MaxDepth = 1,
    DocumentRelationshipTraversalDirection Direction = DocumentRelationshipTraversalDirection.Both,
    IReadOnlyCollection<string>? RelationshipCodes = null,
    int MaxNodes = 200,
    int MaxEdges = 500);
