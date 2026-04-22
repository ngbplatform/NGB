namespace NGB.ReferenceRegisters.Contracts;

/// <summary>
/// Idempotency operations for reference register writes.
///
/// Persisted as SMALLINT in reference_register_write_state.operation.
/// </summary>
public enum ReferenceRegisterWriteOperation : short
{
    Post = 1,
    Unpost = 2,
    Repost = 3,
}
