namespace NGB.Core.Documents.Relationships.Graph;

/// <summary>
/// Keyset paging cursor for document relationship edges ordered by
/// created_at_utc DESC, relationship_id DESC.
/// </summary>
public sealed record DocumentRelationshipEdgeCursor(DateTime AfterCreatedAtUtc, Guid AfterRelationshipId);
