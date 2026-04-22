namespace NGB.Persistence.ReferenceRegisters;

/// <summary>
/// Concurrency guard for Independent-mode reference register writes.
///
/// Implementations typically use transaction-scoped advisory locks to serialize writes for the same key.
/// </summary>
public interface IReferenceRegisterKeyLock
{
    /// <summary>
    /// Acquires a transaction-scoped lock for a reference register key.
    /// The lock is released automatically when the active transaction ends.
    /// </summary>
    Task LockKeyAsync(Guid registerId, Guid dimensionSetId, CancellationToken ct = default);
}
