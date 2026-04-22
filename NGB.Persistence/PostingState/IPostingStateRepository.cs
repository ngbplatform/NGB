using NGB.Accounting.PostingState;

namespace NGB.Persistence.PostingState;

public interface IPostingStateRepository
{
    /// <summary>
    /// Attempts to start an operation for the given document.
    /// Must be called inside an active DB transaction.
    ///
    /// Returns:
    /// - Begun: caller may proceed with writing
    /// - AlreadyCompleted: operation has already completed before; treat as idempotent success
    /// - InProgress: operation has started before but not completed (concurrent/duplicate attempt)
    /// </summary>
    Task<PostingStateBeginResult> TryBeginAsync(
        Guid documentId,
        PostingOperation operation,
        DateTime startedAtUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a previously begun operation as completed.
    /// Must be called inside the same DB transaction as the writes it guards.
    /// </summary>
    Task MarkCompletedAsync(
        Guid documentId,
        PostingOperation operation,
        DateTime completedAtUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Clears previously completed technical state for the given document/operation.
    ///
    /// Important:
    /// - this does NOT delete immutable history;
    /// - this only removes the current mutable dedupe/state row so the opposite lifecycle
    ///   transition may execute again after the business state has genuinely changed.
    ///
    /// Must be called inside the same active transaction as the lifecycle write.
    /// </summary>
    Task ClearCompletedStateAsync(Guid documentId, PostingOperation operation, CancellationToken ct = default);
}
