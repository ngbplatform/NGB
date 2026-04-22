namespace NGB.ReferenceRegisters;

/// <summary>
/// Reference Register periodicity.
///
/// IMPORTANT:
/// - Persisted as SMALLINT in PostgreSQL (reference_registers.periodicity).
/// - Values are part of the data contract; do not reorder.
/// </summary>
public enum ReferenceRegisterPeriodicity : short
{
    NonPeriodic = 0,
    Second = 1,
    Day = 2,
    Month = 3,
    Quarter = 4,
    Year = 5,
}
