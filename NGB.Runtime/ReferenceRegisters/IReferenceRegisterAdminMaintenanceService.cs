using NGB.ReferenceRegisters.Contracts;

namespace NGB.Runtime.ReferenceRegisters;

/// <summary>
/// Maintenance operations for Reference Registers.
///
/// This is an internal Runtime abstraction used by <see cref="IReferenceRegisterAdminEndpoint"/>.
/// </summary>
public interface IReferenceRegisterAdminMaintenanceService
{
    Task<ReferenceRegisterPhysicalSchemaHealth?> EnsurePhysicalSchemaByIdAsync(
        Guid registerId,
        CancellationToken ct = default);

    Task<ReferenceRegisterPhysicalSchemaHealthReport> EnsurePhysicalSchemaForAllAsync(
        CancellationToken ct = default);
}
