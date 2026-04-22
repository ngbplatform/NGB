using NGB.Core.Documents;

namespace NGB.Runtime.Documents;

/// <summary>
/// Application service for platform-level document relationships.
///
/// Semantics:
/// - Relationships are directed: <c>fromDocumentId</c> -> <c>toDocumentId</c>.
/// - The <paramref name="relationshipCode"/> is a module-defined, case-insensitive code.
/// - Writes are allowed only while the "from" document is in Draft status.
/// - Create/Delete operations are idempotent; no-op operations are not audited.
/// </summary>
public interface IDocumentRelationshipService
{
    Task<bool> CreateAsync(
        Guid fromDocumentId,
        Guid toDocumentId,
        string relationshipCode,
        bool manageTransaction = true,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(
        Guid fromDocumentId,
        Guid toDocumentId,
        string relationshipCode,
        bool manageTransaction = true,
        CancellationToken ct = default);

    Task<IReadOnlyList<DocumentRelationshipRecord>> ListOutgoingAsync(
        Guid fromDocumentId,
        CancellationToken ct = default);

    Task<IReadOnlyList<DocumentRelationshipRecord>> ListIncomingAsync(
        Guid toDocumentId,
        CancellationToken ct = default);
}
