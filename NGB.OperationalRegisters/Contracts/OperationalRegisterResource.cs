namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Operational register resource (aka "measure") definition.
///
/// Table: operational_register_resources
///
/// Notes:
/// - Resources are numeric (stored as NUMERIC(28,8)) for accumulation-style registers.
/// - <see cref="ColumnCode"/> is a safe SQL identifier used as a physical column name
///   in per-register movements/turnovers/balances tables.
/// </summary>
public sealed record OperationalRegisterResource(
    string Code,
    string CodeNorm,
    string ColumnCode,
    string Name,
    int Ordinal);
