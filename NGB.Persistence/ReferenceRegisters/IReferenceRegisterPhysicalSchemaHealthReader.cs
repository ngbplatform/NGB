using NGB.ReferenceRegisters.Contracts;

namespace NGB.Persistence.ReferenceRegisters;

/// <summary>
/// Diagnostics reader for the *physical* (dynamic) per-register table:
/// refreg_&lt;table_code&gt;__records.
///
/// Notes:
/// - Read-only.
/// - Does not create tables.
/// - Intended for admin UX diagnostics and CI assertions.
/// </summary>
public interface IReferenceRegisterPhysicalSchemaHealthReader
{
    /// <summary>
    /// Returns a schema health report for all known Reference Registers.
    /// </summary>
    Task<ReferenceRegisterPhysicalSchemaHealthReport> GetReportAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a schema health report for a single Reference Register.
    /// Returns null when register does not exist.
    /// </summary>
    Task<ReferenceRegisterPhysicalSchemaHealth?> GetByRegisterIdAsync(Guid registerId, CancellationToken ct = default);
}
