using NGB.OperationalRegisters.Contracts;

namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// Diagnostics reader for the *physical* (dynamic) per-register tables:
/// opreg_&lt;table_code&gt;__movements / __turnovers / __balances.
///
/// Notes:
/// - Read-only.
/// - Does not create tables.
/// - Intended for admin UX diagnostics and CI assertions.
/// </summary>
public interface IOperationalRegisterPhysicalSchemaHealthReader
{
    /// <summary>
    /// Returns a schema health report for all known Operational Registers.
    /// </summary>
    Task<OperationalRegisterPhysicalSchemaHealthReport> GetReportAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a schema health report for a single Operational Register.
    /// Returns null when register does not exist.
    /// </summary>
    Task<OperationalRegisterPhysicalSchemaHealth?> GetByRegisterIdAsync(Guid registerId, CancellationToken ct = default);
}
