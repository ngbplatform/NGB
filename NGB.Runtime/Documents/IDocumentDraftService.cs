namespace NGB.Runtime.Documents;

/// <summary>
/// Draft-only document application service.
/// Exposed as an abstraction to keep consumers provider-agnostic and concrete-free.
/// </summary>
public interface IDocumentDraftService
{
    Task<Guid> CreateDraftAsync(
        string typeCode,
        string? number,
        DateTime dateUtc,
        bool manageTransaction = true,
        bool suppressAudit = false,
        CancellationToken ct = default);

    /// <summary>
    /// Updates common draft header fields stored in the document registry (documents).
    ///
    /// If the document type declares typed storage, the platform will also invoke a typed update hook
    /// in the same transaction via <see cref="NGB.Persistence.Documents.Storage.IDocumentTypeDraftFullUpdater" />.
    ///
    /// Semantics:
    /// - Only Draft documents can be updated.
    /// - Returns true if any field changed; false if this was a no-op.
    /// - <paramref name="number"/>: null = keep; whitespace/empty = clear; otherwise trimmed value.
    /// - <paramref name="dateUtc"/>: null = keep; otherwise must be UTC.
    /// </summary>
    Task<bool> UpdateDraftAsync(
        Guid documentId,
        string? number,
        DateTime? dateUtc,
        bool manageTransaction = true,
        CancellationToken ct = default);

    /// <summary>
    /// Hard-deletes a Draft document (common registry row + per-type typed storage, if any).
    ///
    /// Semantics:
    /// - Allowed statuses: Draft and MarkedForDeletion.
    /// - Returns true if deleted; false if the document does not exist (idempotent no-op).
    /// - Posted documents cannot be deleted.
    /// </summary>
    Task<bool> DeleteDraftAsync(Guid documentId, bool manageTransaction = true, CancellationToken ct = default);
}
