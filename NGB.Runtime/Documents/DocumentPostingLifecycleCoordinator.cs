using NGB.Accounting.PostingState;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.PostingState;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Documents;

/// <summary>
/// Owns document lifecycle technical coordination for Post / Unpost / Repost.
///
/// Architecture intent:
/// - document-level lifecycle state/history is the source of truth for whether a lifecycle attempt may proceed;
/// - subsystem posting/write logs remain technical local dedupe for accounting / OR / RR engines;
/// - this coordinator keeps opposite-operation technical state re-armed after genuine lifecycle transitions.
///
/// Rules:
/// - immutable history is never deleted;
/// - completed CURRENT-operation state is NOT self-healed anymore;
/// - if document lifecycle state says the operation is already completed while the document workflow says it should proceed,
///   that is treated as an inconsistency and must fail fast rather than silently no-op;
/// - exception: Repost is repeatable while the document remains Posted, so an already-completed
///   Repost state is treated as a strict technical no-op.
/// </summary>
internal sealed class DocumentPostingLifecycleCoordinator(
    IDocumentOperationStateRepository documentOperationStateRepository,
    IPostingStateRepository postingStateRepository,
    IOperationalRegisterWriteStateRepository opregWriteStateRepository,
    IReferenceRegisterWriteStateRepository refregWriteStateRepository,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    /// <summary>
    /// Begins a document lifecycle attempt.
    /// Returns <see cref="DocumentLifecycleBeginResult.NoOp"/> for duplicate Repost calls,
    /// because workflow state alone cannot distinguish a fresh Repost from a technical retry once
    /// the document remains Posted.
    /// </summary>
    public async Task<DocumentLifecycleBeginResult> BeginAsync(
        Guid documentId,
        PostingOperation operation,
        CancellationToken ct)
    {
        var begin = await documentOperationStateRepository.TryBeginAsync(documentId, operation, _timeProvider.GetUtcNowDateTime(), ct);

        if (begin == PostingStateBeginResult.Begun)
            return DocumentLifecycleBeginResult.Begun;

        if (begin == PostingStateBeginResult.InProgress)
            throw new PostingAlreadyInProgressException(documentId, operation);

        if (operation == PostingOperation.Repost)
            return DocumentLifecycleBeginResult.NoOp;

        throw BuildLifecycleStateConflict(documentId, operation, layer: "document");
    }

    /// <summary>
    /// Executes accounting posting under document-level lifecycle control.
    /// If the lower accounting layer reports AlreadyCompleted after document lifecycle begin succeeded,
    /// the system is inconsistent and must fail fast.
    /// </summary>
    public async Task ExecuteAccountingAsync(
        Guid documentId,
        PostingOperation operation,
        Func<Task<PostingResult>> execute,
        CancellationToken ct)
    {
        if (execute is null)
            throw new NgbArgumentRequiredException(nameof(execute));

        var result = await execute();
        if (result == PostingResult.Executed)
            return;

        if (result == PostingResult.AlreadyCompleted)
            throw BuildLifecycleStateConflict(documentId, operation, layer: "accounting");
    }

    /// <summary>
    /// Abandons a begun lifecycle attempt when orchestration discovers a strict no-op late in the flow.
    /// Preserves immutable history and removes only the current uncompleted technical state row.
    /// </summary>
    public Task CancelAsync(
        Guid documentId,
        PostingOperation operation,
        CancellationToken ct)
        => documentOperationStateRepository.ClearInProgressStateAsync(documentId, operation, ct);

    /// <summary>
    /// Completes the current lifecycle attempt and re-arms opposite technical state for the next real cycle.
    /// Must be called inside the same transaction as business side effects and document status update.
    /// </summary>
    public async Task CompleteSuccessfulTransitionAsync(
        Guid documentId,
        PostingOperation operation,
        CancellationToken ct)
    {
        await documentOperationStateRepository.MarkCompletedAsync(documentId, operation, _timeProvider.GetUtcNowDateTime(), ct);

        await RearmDocumentOppositeStateAsync(documentId, operation, ct);
        await RearmSubsystemOppositeStateAsync(documentId, operation, ct);
    }

    private Task RearmDocumentOppositeStateAsync(Guid documentId, PostingOperation operation, CancellationToken ct)
        => operation switch
        {
            PostingOperation.Post => ClearDocumentCompletedStateAsync(documentId, [PostingOperation.Unpost], ct),
            PostingOperation.Unpost => ClearDocumentCompletedStateAsync(documentId, [PostingOperation.Post, PostingOperation.Repost], ct),
            _ => Task.CompletedTask
        };

    private async Task ClearDocumentCompletedStateAsync(
        Guid documentId,
        IReadOnlyList<PostingOperation> operations,
        CancellationToken ct)
    {
        foreach (var op in operations)
            await documentOperationStateRepository.ClearCompletedStateAsync(documentId, op, ct);
    }

    private Task RearmSubsystemOppositeStateAsync(Guid documentId, PostingOperation operation, CancellationToken ct)
        => operation switch
        {
            PostingOperation.Post => RearmSubsystemOperationsAsync(
                documentId,
                postingOperations: [PostingOperation.Unpost],
                opregOperations: [OperationalRegisterWriteOperation.Unpost],
                refregOperations: [ReferenceRegisterWriteOperation.Unpost],
                ct),

            PostingOperation.Unpost => RearmSubsystemOperationsAsync(
                documentId,
                postingOperations: [PostingOperation.Post, PostingOperation.Repost],
                opregOperations: [OperationalRegisterWriteOperation.Post, OperationalRegisterWriteOperation.Repost],
                refregOperations: [ReferenceRegisterWriteOperation.Post, ReferenceRegisterWriteOperation.Repost],
                ct),

            PostingOperation.Repost => Task.CompletedTask,
            _ => Task.CompletedTask
        };

    private async Task RearmSubsystemOperationsAsync(
        Guid documentId,
        IReadOnlyList<PostingOperation> postingOperations,
        IReadOnlyList<OperationalRegisterWriteOperation> opregOperations,
        IReadOnlyList<ReferenceRegisterWriteOperation> refregOperations,
        CancellationToken ct)
    {
        foreach (var op in postingOperations)
            await postingStateRepository.ClearCompletedStateAsync(documentId, op, ct);

        foreach (var op in opregOperations)
            await opregWriteStateRepository.ClearCompletedStateByDocumentAsync(documentId, op, ct);

        foreach (var op in refregOperations)
            await refregWriteStateRepository.ClearCompletedStateByDocumentAsync(documentId, op, ct);
    }

    private static NgbInvariantViolationException BuildLifecycleStateConflict(
        Guid documentId,
        PostingOperation operation,
        string layer)
        => new(
            "Document lifecycle state is inconsistent with technical operation state.",
            new Dictionary<string, object?>
            {
                ["documentId"] = documentId,
                ["operation"] = operation.ToString(),
                ["layer"] = layer
            });
}
