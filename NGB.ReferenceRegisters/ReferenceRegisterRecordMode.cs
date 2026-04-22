namespace NGB.ReferenceRegisters;

/// <summary>
/// Reference Register recording mode.
///
/// IMPORTANT:
/// - Persisted as SMALLINT in PostgreSQL (reference_registers.record_mode).
/// - Values are part of the data contract; do not reorder.
/// </summary>
public enum ReferenceRegisterRecordMode : short
{
    Independent = 0,
    SubordinateToRecorder = 1,
}
