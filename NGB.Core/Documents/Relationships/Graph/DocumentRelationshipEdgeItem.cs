namespace NGB.Core.Documents.Relationships.Graph;

/// <summary>
/// Relationship edge plus the "other" document header (To for outgoing, From for incoming).
/// </summary>
public sealed record DocumentRelationshipEdgeItem(
    Guid RelationshipId,
    Guid FromDocumentId,
    Guid ToDocumentId,
    string RelationshipCode,
    string RelationshipCodeNorm,
    DateTime CreatedAtUtc,
    DocumentRelationshipDocumentHeader OtherDocument);
