namespace NGB.ReferenceRegisters.Contracts;

/// <summary>
/// Write model for creating/updating reference register metadata.
///
/// Table: reference_registers
/// </summary>
public sealed record ReferenceRegisterUpsert(
    Guid RegisterId,
    string Code,
    string Name,
    ReferenceRegisterPeriodicity Periodicity,
    ReferenceRegisterRecordMode RecordMode);
