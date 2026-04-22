using NGB.Accounting.PostingState;

namespace NGB.Persistence.Documents;

/// <summary>
/// Technical lifecycle state gate for document operations (Post / Unpost / Repost).
///
/// Responsibilities:
/// - coordinates in-flight / retry / stale-takeover semantics for document lifecycle attempts;
/// - persists ONLY ephemeral state in the current row set;
/// - implementation must preserve immutable history separately.
///
/// This repository is intentionally document-level. It is the source of truth for deciding
/// whether a document lifecycle operation may proceed in the current state.
/// </summary>
public interface IDocumentOperationStateRepository
{
    Task<PostingStateBeginResult> TryBeginAsync(
        Guid documentId,
        PostingOperation operation,
        DateTime startedAtUtc,
        CancellationToken ct = default);

    Task MarkCompletedAsync(
        Guid documentId,
        PostingOperation operation,
        DateTime completedAtUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Clears only the mutable technical state row for a completed operation.
    ///
    /// Implementations MUST preserve immutable history.
    /// Used to re-arm a future lifecycle cycle after the document state has genuinely changed.
    /// </summary>
    Task ClearCompletedStateAsync(Guid documentId, PostingOperation operation, CancellationToken ct = default);

    /// <summary>
    /// Clears an uncompleted technical state row for the current attempt.
    ///
    /// This is used only when the lifecycle coordinator must abandon a begun attempt without committing
    /// business side effects (for example, a strict no-op discovered late in orchestration).
    /// Immutable history must be preserved.
    /// </summary>
    Task ClearInProgressStateAsync(Guid documentId, PostingOperation operation, CancellationToken ct = default);
}
