using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// Default implementation of <see cref="IOperationalRegisterAdminEndpoint"/>.
/// </summary>
public sealed class OperationalRegisterAdminEndpoint(
    IOperationalRegisterAdminReadService readService,
    IOperationalRegisterAdminMaintenanceService maintenanceService)
    : IOperationalRegisterAdminEndpoint
{
    public async Task<IReadOnlyList<OperationalRegisterAdminEndpointContracts.RegisterListItemDto>> GetListAsync(
        CancellationToken ct = default)
    {
        var list = await readService.GetListAsync(ct);
        if (list.Count == 0)
            return [];

        return list.Select(x => x.ToDto()).ToArray();
    }

    public async Task<OperationalRegisterAdminEndpointContracts.RegisterDetailsDto?> GetDetailsByIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        registerId.EnsureRequired(nameof(registerId));
        var details = await readService.GetDetailsByIdAsync(registerId, ct);
        return details?.ToDto();
    }

    public async Task<OperationalRegisterAdminEndpointContracts.RegisterDetailsDto?> GetDetailsByCodeAsync(
        string code,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new NgbArgumentRequiredException(nameof(code));

        var details = await readService.GetDetailsByCodeAsync(code, ct);
        return details?.ToDto();
    }

    public async Task<OperationalRegisterAdminEndpointContracts.PhysicalSchemaHealthReportDto> GetPhysicalSchemaHealthReportAsync(
        CancellationToken ct = default)
    {
        var report = await readService.GetPhysicalSchemaHealthReportAsync(ct);
        return report.ToDto();
    }

    public async Task<OperationalRegisterAdminEndpointContracts.PhysicalSchemaHealthDto?> GetPhysicalSchemaHealthByIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        registerId.EnsureRequired(nameof(registerId));
        var health = await readService.GetPhysicalSchemaHealthByIdAsync(registerId, ct);
        return health?.ToDto();
    }

    public async Task<OperationalRegisterAdminEndpointContracts.PhysicalSchemaHealthDto?> EnsurePhysicalSchemaByIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        registerId.EnsureRequired(nameof(registerId));
        var health = await maintenanceService.EnsurePhysicalSchemaByIdAsync(registerId, ct);
        return health?.ToDto();
    }

    public async Task<OperationalRegisterAdminEndpointContracts.PhysicalSchemaHealthReportDto> EnsurePhysicalSchemaForAllAsync(
        CancellationToken ct = default)
    {
        var report = await maintenanceService.EnsurePhysicalSchemaForAllAsync(ct);
        return report.ToDto();
    }

    public async Task<OperationalRegisterAdminEndpointContracts.FinalizationDto?> GetFinalizationAsync(
        Guid registerId,
        DateOnly periodMonth,
        CancellationToken ct = default)
    {
        registerId.EnsureRequired(nameof(registerId));
        periodMonth.EnsureMonthStart(nameof(periodMonth));

        var f = await readService.GetFinalizationAsync(registerId, periodMonth, ct);
        return f?.ToDto();
    }

    public async Task<IReadOnlyList<OperationalRegisterAdminEndpointContracts.FinalizationDto>> GetDirtyFinalizationsByIdAsync(
        Guid registerId,
        int limit = 100,
        CancellationToken ct = default)
    {
        registerId.EnsureRequired(nameof(registerId));
        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");

        var list = await readService.GetDirtyFinalizationsAsync(registerId, limit, ct);
        if (list.Count == 0)
            return [];

        return list.Select(x => x.ToDto()).ToArray();
    }

    public async Task<IReadOnlyList<OperationalRegisterAdminEndpointContracts.FinalizationDto>> GetBlockedFinalizationsByIdAsync(
        Guid registerId,
        int limit = 100,
        CancellationToken ct = default)
    {
        registerId.EnsureRequired(nameof(registerId));
        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");

        var list = await readService.GetBlockedFinalizationsAsync(registerId, limit, ct);
        if (list.Count == 0)
            return [];

        return list.Select(x => x.ToDto()).ToArray();
    }

    public async Task<IReadOnlyList<OperationalRegisterAdminEndpointContracts.FinalizationDto>> GetDirtyFinalizationsAcrossAllAsync(
        int limit = 100,
        CancellationToken ct = default)
    {
        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");

        var list = await readService.GetDirtyFinalizationsAcrossAllAsync(limit, ct);
        if (list.Count == 0)
            return [];

        return list.Select(x => x.ToDto()).ToArray();
    }

    public async Task<IReadOnlyList<OperationalRegisterAdminEndpointContracts.FinalizationDto>> GetBlockedFinalizationsAcrossAllAsync(
        int limit = 100,
        CancellationToken ct = default)
    {
        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");

        var list = await readService.GetBlockedFinalizationsAcrossAllAsync(limit, ct);
        if (list.Count == 0)
            return [];

        return list.Select(x => x.ToDto()).ToArray();
    }

    public async Task MarkFinalizationDirtyAsync(Guid registerId, DateOnly periodMonth, CancellationToken ct = default)
    {
        registerId.EnsureRequired(nameof(registerId));
        periodMonth.EnsureMonthStart(nameof(periodMonth));

        await maintenanceService.MarkFinalizationDirtyAsync(registerId, periodMonth, ct);
    }

    public Task<int> FinalizeDirtyAsync(int maxItems = 50, CancellationToken ct = default)
    {
        if (maxItems <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(maxItems), maxItems, "MaxItems must be positive.");

        return maintenanceService.FinalizeDirtyAsync(maxItems, ct);
    }

    public Task<int> FinalizeRegisterDirtyAsync(Guid registerId, int maxPeriods = 50, CancellationToken ct = default)
    {
        registerId.EnsureRequired(nameof(registerId));
        if (maxPeriods <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(maxPeriods), maxPeriods, "MaxPeriods must be positive.");

        return maintenanceService.FinalizeRegisterDirtyAsync(registerId, maxPeriods, ct);
    }
}
