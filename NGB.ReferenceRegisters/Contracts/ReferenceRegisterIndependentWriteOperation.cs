namespace NGB.ReferenceRegisters.Contracts;

/// <summary>
/// Idempotency operations for Independent-mode reference register writes.
///
/// Persisted as SMALLINT in reference_register_independent_write_state.operation.
/// </summary>
public enum ReferenceRegisterIndependentWriteOperation : short
{
    Upsert = 1,
    Tombstone = 2,
}
