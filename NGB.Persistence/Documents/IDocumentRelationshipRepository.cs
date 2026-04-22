using NGB.Core.Documents;

namespace NGB.Persistence.Documents;

/// <summary>
/// Persistence contract for platform-level document relationships.
///
/// Writes require an active unit of work transaction.
/// Reads may be executed without a transaction.
/// </summary>
public interface IDocumentRelationshipRepository
{
    Task<bool> TryCreateAsync(DocumentRelationshipRecord relationship, CancellationToken ct = default);

    Task<DocumentRelationshipRecord?> GetAsync(Guid relationshipId, CancellationToken ct = default);

    /// <summary>
    /// Returns an arbitrary outgoing relationship for <paramref name="fromDocumentId"/> with the given code.
    /// Intended for enforcing relationship type cardinality constraints.
    /// </summary>
    Task<DocumentRelationshipRecord?> GetSingleOutgoingByCodeNormAsync(
        Guid fromDocumentId,
        string relationshipCodeNorm,
        CancellationToken ct = default);

    /// <summary>
    /// Returns an arbitrary incoming relationship for <paramref name="toDocumentId"/> with the given code.
    /// Intended for enforcing relationship type cardinality constraints.
    /// </summary>
    Task<DocumentRelationshipRecord?> GetSingleIncomingByCodeNormAsync(
        Guid toDocumentId,
        string relationshipCodeNorm,
        CancellationToken ct = default);

    Task<bool> TryDeleteAsync(Guid relationshipId, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentRelationshipRecord>> ListOutgoingAsync(
        Guid fromDocumentId,
        CancellationToken ct = default);

    Task<IReadOnlyList<DocumentRelationshipRecord>> ListIncomingAsync(
        Guid toDocumentId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> if a directed path exists from <paramref name="fromDocumentId"/> to
    /// <paramref name="toDocumentId"/> following only relationships with the given
    /// <paramref name="relationshipCodeNorm"/>.
    ///
    /// Intended for preventing cycles when creating directed relationships.
    /// </summary>
    Task<bool> ExistsPathAsync(
        Guid fromDocumentId,
        Guid toDocumentId,
        string relationshipCodeNorm,
        int maxDepth,
        CancellationToken ct = default);
}
