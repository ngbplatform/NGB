using NGB.OperationalRegisters.Contracts;

namespace NGB.PostgreSql.OperationalRegisters.Internal;

/// <summary>
/// Shared Dapper row for Operational Registers admin reads.
/// Keep property names aligned with SQL aliases.
/// </summary>
internal class OperationalRegisterAdminItemRow
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
