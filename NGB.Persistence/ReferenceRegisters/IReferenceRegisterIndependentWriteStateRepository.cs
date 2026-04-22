using NGB.Accounting.PostingState;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.Persistence.ReferenceRegisters;

/// <summary>
/// Idempotency log for Independent-mode reference register writes.
///
/// One row per (register_id, command_id, operation) written in the same transaction as register records.
/// Allows safe retries on timeouts / crashes.
/// </summary>
public interface IReferenceRegisterIndependentWriteStateRepository
{
    Task<PostingStateBeginResult> TryBeginAsync(
        Guid registerId,
        Guid commandId,
        ReferenceRegisterIndependentWriteOperation operation,
        DateTime startedAtUtc,
        CancellationToken ct = default);

    Task MarkCompletedAsync(
        Guid registerId,
        Guid commandId,
        ReferenceRegisterIndependentWriteOperation operation,
        DateTime completedAtUtc,
        CancellationToken ct = default);
}
