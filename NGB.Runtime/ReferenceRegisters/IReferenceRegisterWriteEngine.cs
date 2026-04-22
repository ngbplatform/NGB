using NGB.ReferenceRegisters.Contracts;

namespace NGB.Runtime.ReferenceRegisters;

/// <summary>
/// Canonical reference register write pipeline:
/// - transaction (optional)
/// - concurrency guards (document)
/// - idempotency begin (reference_register_write_state)
/// - execute write action
/// - mark state completed
/// </summary>
public interface IReferenceRegisterWriteEngine
{
    Task<ReferenceRegisterWriteResult> ExecuteAsync(
        Guid registerId,
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        Func<CancellationToken, Task> writeAction,
        bool manageTransaction = true,
        CancellationToken ct = default);
}
