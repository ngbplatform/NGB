namespace NGB.Core.Documents.Relationships.Graph;

/// <summary>
/// Minimal document header for UI graph/navigation scenarios.
/// </summary>
public sealed record DocumentRelationshipDocumentHeader(
    Guid DocumentId,
    string TypeCode,
    string? Number,
    DateTime DateUtc,
    DocumentStatus Status);
