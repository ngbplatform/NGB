namespace NGB.Metadata.Documents.Hybrid;

/// <summary>
/// Declarative opt-in telling the platform that a scalar head-table document reference field
/// should be mirrored into persisted <c>document_relationships</c> using the specified
/// relationship type code.
/// </summary>
public sealed record MirroredDocumentRelationshipMetadata(string RelationshipCode);
