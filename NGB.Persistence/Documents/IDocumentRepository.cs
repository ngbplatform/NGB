using NGB.Core.Documents;

namespace NGB.Persistence.Documents;

/// <summary>
/// Document registry repository (table: documents).
///
/// IMPORTANT:
/// - For state transitions, use GetForUpdateAsync inside an active transaction to serialize updates.
/// - This repository stores only the common header. Per-type data belongs in separate tables:
///   doc_{type_code}, doc_{type_code}__{part}, ...
/// </summary>
public interface IDocumentRepository
{
    Task CreateAsync(DocumentRecord doc, CancellationToken ct = default);

    Task<DocumentRecord?> GetAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Loads and locks the document row until the current transaction completes (SELECT ... FOR UPDATE).
    /// Requires an active transaction.
    /// </summary>
    Task<DocumentRecord?> GetForUpdateAsync(Guid documentId, CancellationToken ct = default);

    Task UpdateStatusAsync(
        Guid documentId,
        DocumentStatus status,
        DateTime updatedAtUtc,
        DateTime? postedAtUtc,
        DateTime? markedForDeletionAtUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Updates draft header fields stored in the common document registry (documents).
    /// Requires an active transaction.
    /// </summary>
    Task<bool> UpdateDraftHeaderAsync(
        Guid documentId,
        string? number,
        DateTime dateUtc,
        DateTime updatedAtUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Sets the document number once (only if it is currently NULL).
    /// Requires an active transaction.
    /// Returns true when the number was set by this call.
    /// </summary>
    Task<bool> TrySetNumberAsync(
        Guid documentId,
        string number,
        DateTime updatedAtUtc,
        CancellationToken ct = default);
    
    Task<bool> TryDeleteAsync(Guid documentId, CancellationToken ct = default);
}
