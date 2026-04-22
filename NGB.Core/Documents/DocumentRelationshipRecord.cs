using NGB.Core.Base;

namespace NGB.Core.Documents;

/// <summary>
/// Directed relationship (edge) between two documents.
///
/// The edge is directed: <see cref="FromDocumentId"/> -> <see cref="ToDocumentId"/>.
/// The semantics of <see cref="RelationshipCode"/> are defined by modules (for example: "based_on", "reversal_of").
/// </summary>
public sealed class DocumentRelationshipRecord : Entity
{
    public required Guid FromDocumentId { get; init; }
    public required Guid ToDocumentId { get; init; }
    public required string RelationshipCode { get; init; }
    public required string RelationshipCodeNorm { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}
