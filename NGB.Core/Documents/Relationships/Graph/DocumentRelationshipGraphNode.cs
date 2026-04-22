namespace NGB.Core.Documents.Relationships.Graph;

/// <summary>
/// A document node in the relationship graph, enriched with basic header info.
/// Depth is the BFS distance from the root document.
/// </summary>
public sealed record DocumentRelationshipGraphNode(
    Guid DocumentId,
    string TypeCode,
    string? Number,
    DateTime DateUtc,
    DocumentStatus Status,
    int Depth);
