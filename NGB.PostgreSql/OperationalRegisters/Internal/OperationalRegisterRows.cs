using NGB.OperationalRegisters.Contracts;

namespace NGB.PostgreSql.OperationalRegisters.Internal;

// Shared Dapper row models for operational_registers reads.
// Kept internal to avoid leaking infrastructure types outside PostgreSql project.
internal class OperationalRegisterRow
{
    public Guid RegisterId { get; init; }
    public string Code { get; init; } = null!;
    public string CodeNorm { get; init; } = null!;
    public string TableCode { get; init; } = null!;
    public string Name { get; init; } = null!;
    public bool HasMovements { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }

    public OperationalRegisterAdminItem ToItem() => new(
        RegisterId,
        Code,
        CodeNorm,
        TableCode,
        Name,
        HasMovements,
        CreatedAtUtc,
        UpdatedAtUtc);
}
