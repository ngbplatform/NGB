using NGB.Accounting.Posting;

namespace NGB.Runtime.Documents;

/// <summary>
/// Document lifecycle + accounting posting orchestration.
/// Exposed as an abstraction to keep consumers provider-agnostic and concrete-free.
///
/// Draft lifecycle (create/update/delete) is handled by <see cref="IDocumentDraftService"/>.
/// </summary>
public interface IDocumentPostingService
{
    Task PostAsync(
        Guid documentId,
        Func<IAccountingPostingContext, CancellationToken, Task> postingAction,
        CancellationToken ct = default);

    /// <summary>
    /// Posts a Draft document using the posting handler defined in the document type definition.
    /// When <paramref name="manageTransaction"/> is false, the caller must ensure an active transaction.
    /// </summary>
    Task PostAsync(Guid documentId, bool manageTransaction, CancellationToken ct = default);

    /// <summary>
    /// Posts a Draft document using the posting handler defined in the document type definition.
    /// </summary>
    Task PostAsync(Guid documentId, CancellationToken ct = default);

    Task UnpostAsync(Guid documentId, CancellationToken ct = default);

    Task RepostAsync(
        Guid documentId,
        Func<IAccountingPostingContext, CancellationToken, Task> postNew,
        CancellationToken ct = default);

    /// <summary>
    /// Moves a Draft document into the MarkedForDeletion state.
    ///
    /// Semantics:
    /// - Allowed status: Draft.
    /// - If already MarkedForDeletion, this is an idempotent no-op.
    /// - Posted documents cannot be marked for deletion.
    /// </summary>
    Task MarkForDeletionAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Moves a Draft document from MarkedForDeletion back to Draft.
    ///
    /// Semantics:
    /// - Allowed status: MarkedForDeletion.
    /// - If already Draft, this is an idempotent no-op.
    /// - Posted documents cannot be unmarked.
    /// </summary>
    Task UnmarkForDeletionAsync(Guid documentId, CancellationToken ct = default);
}
