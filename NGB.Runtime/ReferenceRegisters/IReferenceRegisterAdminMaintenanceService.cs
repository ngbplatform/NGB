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

    Task<ReferenceRegisterPhysicalSchemaHealthReport> EnsurePhysicalSchemaForAllAsync(CancellationToken ct = default);
}

/// <summary>
/// Optional optimized maintenance boundary for bounded setup/bootstrap batches.
/// </summary>
public interface IReferenceRegisterAdminBatchMaintenanceService : IReferenceRegisterAdminMaintenanceService
{
    Task EnsurePhysicalSchemasByIdsAsync(IReadOnlyCollection<Guid> registerIds, CancellationToken ct = default);
}

public static class ReferenceRegisterAdminMaintenanceServiceExtensions
{
    public static async Task EnsurePhysicalSchemasByIdsAsync(
        this IReferenceRegisterAdminMaintenanceService maintenance,
        IReadOnlyCollection<Guid> registerIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(maintenance);
        ArgumentNullException.ThrowIfNull(registerIds);

        if (maintenance is IReferenceRegisterAdminBatchMaintenanceService batchMaintenance)
        {
            await batchMaintenance.EnsurePhysicalSchemasByIdsAsync(registerIds, ct);
            return;
        }

        foreach (var registerId in registerIds)
        {
            await maintenance.EnsurePhysicalSchemaByIdAsync(registerId, ct);
        }
    }
}
