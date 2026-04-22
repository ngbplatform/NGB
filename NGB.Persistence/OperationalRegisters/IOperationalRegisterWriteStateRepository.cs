using NGB.Accounting.PostingState;
using NGB.OperationalRegisters.Contracts;

namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// Persistence boundary for operational register idempotency state.
///
/// Table:
/// - operational_register_write_state
///
/// Notes:
/// - Must be used in the same DB transaction as register writes.
/// - Semantics mirror the accounting posting state, but the key is (register_id, document_id, operation).
/// </summary>
public interface IOperationalRegisterWriteStateRepository
{
    Task<PostingStateBeginResult> TryBeginAsync(
        Guid registerId,
        Guid documentId,
        OperationalRegisterWriteOperation operation,
        DateTime startedAtUtc,
        CancellationToken ct = default);

    Task MarkCompletedAsync(
        Guid registerId,
        Guid documentId,
        OperationalRegisterWriteOperation operation,
        DateTime completedAtUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Returns distinct register identifiers that have at least one completed Post/Repost write state entry for the given document.
    ///
    /// Used by document lifecycle (Unpost/Repost) to discover which registers must be storned, without scanning per-register movements tables.
    /// </summary>
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
        OperationalRegisterWriteOperation operation,
        CancellationToken ct = default);
}
