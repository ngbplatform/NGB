namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Logical write operation for operational registers idempotency control.
/// Values are persisted to DB (SMALLINT), so treat them as stable.
///
/// Table: operational_register_write_state
/// </summary>
public enum OperationalRegisterWriteOperation : short
{
    Post = 1,
    Unpost = 2,
    Repost = 3
}
