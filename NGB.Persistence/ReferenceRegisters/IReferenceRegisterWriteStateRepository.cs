using NGB.Accounting.PostingState;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.Persistence.ReferenceRegisters;

/// <summary>
/// Persistence boundary for reference register idempotency state.
///
/// Table: reference_register_write_state
/// Key: (register_id, document_id, operation)
/// </summary>
public interface IReferenceRegisterWriteStateRepository
{
    Task<PostingStateBeginResult> TryBeginAsync(
        Guid registerId,
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        DateTime startedAtUtc,
        CancellationToken ct = default);

    Task MarkCompletedAsync(
        Guid registerId,
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        DateTime completedAtUtc,
        CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetRegisterIdsByDocumentAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Clears completed technical state rows for the given document/operation so the opposite
    /// lifecycle transition can execute again after state has genuinely changed.
    ///
    /// Important: immutable history remains preserved separately; this only clears mutable
    /// dedupe/state rows. Must be called inside the same active transaction as the lifecycle write.
    /// </summary>
    Task ClearCompletedStateByDocumentAsync(
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        CancellationToken ct = default);
}
