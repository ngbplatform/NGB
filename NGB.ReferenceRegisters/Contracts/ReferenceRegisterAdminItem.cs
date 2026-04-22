namespace NGB.ReferenceRegisters.Contracts;

/// <summary>
/// Admin read model for reference register metadata.
///
/// Source: reference_registers
/// </summary>
public sealed record ReferenceRegisterAdminItem(
    Guid RegisterId,
    string Code,
    string CodeNorm,
    string TableCode,
    string Name,
    ReferenceRegisterPeriodicity Periodicity,
    ReferenceRegisterRecordMode RecordMode,
    bool HasRecords,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
