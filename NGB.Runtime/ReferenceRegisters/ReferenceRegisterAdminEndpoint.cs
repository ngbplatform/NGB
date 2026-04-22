using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.ReferenceRegisters;

/// <summary>
/// Default implementation of <see cref="IReferenceRegisterAdminEndpoint"/>.
/// </summary>
public sealed class ReferenceRegisterAdminEndpoint(
    IReferenceRegisterAdminReadService readService,
    IReferenceRegisterAdminMaintenanceService maintenanceService)
    : IReferenceRegisterAdminEndpoint
{
    public async Task<IReadOnlyList<ReferenceRegisterAdminEndpointContracts.RegisterListItemDto>> GetListAsync(
        CancellationToken ct = default)
    {
        var list = await readService.GetListAsync(ct);
        if (list.Count == 0)
            return [];

        return list.Select(x =>
                new ReferenceRegisterAdminEndpointContracts.RegisterListItemDto(
                    x.Register.ToDto(),
                    x.FieldsCount,
                    x.DimensionRulesCount))
            .ToArray();
    }

    public async Task<ReferenceRegisterAdminEndpointContracts.RegisterDetailsDto?> GetDetailsByIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        var details = await readService.GetDetailsByIdAsync(registerId, ct);
        return details?.ToDto();
    }

    public async Task<ReferenceRegisterAdminEndpointContracts.RegisterDetailsDto?> GetDetailsByCodeAsync(
        string code,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new NgbArgumentRequiredException(nameof(code));

        var details = await readService.GetDetailsByCodeAsync(code, ct);
        return details?.ToDto();
    }

    public async Task<ReferenceRegisterAdminEndpointContracts.PhysicalSchemaHealthReportDto> GetPhysicalSchemaHealthReportAsync(
        CancellationToken ct = default)
    {
        var report = await readService.GetPhysicalSchemaHealthReportAsync(ct);
        return report.ToDto();
    }

    public async Task<ReferenceRegisterAdminEndpointContracts.PhysicalSchemaHealthDto?> GetPhysicalSchemaHealthByIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        var health = await readService.GetPhysicalSchemaHealthByIdAsync(registerId, ct);
        return health?.ToDto();
    }

    public async Task<ReferenceRegisterAdminEndpointContracts.PhysicalSchemaHealthDto?> EnsurePhysicalSchemaByIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        var health = await maintenanceService.EnsurePhysicalSchemaByIdAsync(registerId, ct);
        return health?.ToDto();
    }

    public async Task<ReferenceRegisterAdminEndpointContracts.PhysicalSchemaHealthReportDto> EnsurePhysicalSchemaForAllAsync(
        CancellationToken ct = default)
    {
        var report = await maintenanceService.EnsurePhysicalSchemaForAllAsync(ct);
        return report.ToDto();
    }
}
