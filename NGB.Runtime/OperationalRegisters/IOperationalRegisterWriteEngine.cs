using NGB.OperationalRegisters.Contracts;

namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// Orchestrates atomic operational register writes with idempotency and dirty-marking.
///
/// Responsibilities:
/// - Runs inside a DB transaction (optionally managed by this service).
/// - Uses operational_register_write_state for retry-safe idempotency per (register_id, document_id, operation).
/// - Acquires concurrency guards (document lock + month locks for affected periods).
/// - Marks affected register periods as Dirty (operational_register_finalizations).
/// </summary>
public interface IOperationalRegisterWriteEngine
{
    Task<OperationalRegisterWriteResult> ExecuteAsync(
        Guid registerId,
        Guid documentId,
        OperationalRegisterWriteOperation operation,
        IReadOnlyCollection<DateOnly>? affectedPeriods,
        Func<CancellationToken, Task> writeAction,
        bool manageTransaction = true,
        CancellationToken ct = default);
}
