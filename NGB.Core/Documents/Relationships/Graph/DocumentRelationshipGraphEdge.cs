namespace NGB.Core.Documents.Relationships.Graph;

public sealed record DocumentRelationshipGraphEdge(
    Guid RelationshipId,
    Guid FromDocumentId,
    Guid ToDocumentId,
    string RelationshipCode,
    string RelationshipCodeNorm,
    DateTime CreatedAtUtc);
