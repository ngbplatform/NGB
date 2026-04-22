namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Admin-facing projection of an operational register (table: operational_registers).
///
/// Notes:
/// - Lifecycle is intentionally minimal for now (no is_active/is_deleted).
/// - Code uniqueness is enforced case-insensitively in DB via generated column <c>code_norm</c>.
/// </summary>
public sealed record OperationalRegisterAdminItem(
    Guid RegisterId,
    string Code,
    string CodeNorm,
    string TableCode,
    string Name,
    bool HasMovements,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
