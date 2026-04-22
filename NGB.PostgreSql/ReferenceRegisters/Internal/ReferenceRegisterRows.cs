using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.PostgreSql.ReferenceRegisters.Internal;

// Shared Dapper row models for reference_registers reads.
// Kept internal to avoid leaking infrastructure types outside PostgreSql project.
internal class ReferenceRegisterRow
{
    public Guid RegisterId { get; init; }
    public string Code { get; init; } = null!;
    public string CodeNorm { get; init; } = null!;
    public string TableCode { get; init; } = null!;
    public string Name { get; init; } = null!;
    public short Periodicity { get; init; }
    public short RecordMode { get; init; }
    public bool HasRecords { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }

    public ReferenceRegisterPeriodicity PeriodicityEnum => (ReferenceRegisterPeriodicity)Periodicity;
    public ReferenceRegisterRecordMode RecordModeEnum => (ReferenceRegisterRecordMode)RecordMode;

    public ReferenceRegisterAdminItem ToItem() => new(
        RegisterId,
        Code,
        CodeNorm,
        TableCode,
        Name,
        PeriodicityEnum,
        RecordModeEnum,
        HasRecords,
        CreatedAtUtc,
        UpdatedAtUtc);
}
