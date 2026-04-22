using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;

namespace NGB.Runtime.OperationalRegisters;

public sealed class OperationalRegisterAdminReadService(
    IOperationalRegisterAdminReader reader,
    IOperationalRegisterPhysicalSchemaHealthReader healthReader,
    IOperationalRegisterFinalizationRepository finalizations)
    : IOperationalRegisterAdminReadService
{
    public Task<IReadOnlyList<OperationalRegisterAdminListItem>> GetListAsync(CancellationToken ct = default)
        => reader.GetListAsync(ct);

    public Task<OperationalRegisterAdminDetails?> GetDetailsByIdAsync(Guid registerId, CancellationToken ct = default)
        => reader.GetDetailsByIdAsync(registerId, ct);

    public Task<OperationalRegisterAdminDetails?> GetDetailsByCodeAsync(string code, CancellationToken ct = default)
        => reader.GetDetailsByCodeAsync(code, ct);

    public Task<OperationalRegisterPhysicalSchemaHealthReport> GetPhysicalSchemaHealthReportAsync(CancellationToken ct = default)
        => healthReader.GetReportAsync(ct);

    public Task<OperationalRegisterPhysicalSchemaHealth?> GetPhysicalSchemaHealthByIdAsync(Guid registerId, CancellationToken ct = default)
        => healthReader.GetByRegisterIdAsync(registerId, ct);

    public Task<OperationalRegisterFinalization?> GetFinalizationAsync(
        Guid registerId,
        DateOnly periodMonth,
        CancellationToken ct = default)
        => finalizations.GetAsync(registerId, periodMonth, ct);

    public Task<IReadOnlyList<OperationalRegisterFinalization>> GetDirtyFinalizationsAsync(
        Guid registerId,
        int limit = 100,
        CancellationToken ct = default)
        => finalizations.GetDirtyAsync(registerId, limit, ct);

    public Task<IReadOnlyList<OperationalRegisterFinalization>> GetBlockedFinalizationsAsync(
        Guid registerId,
        int limit = 100,
        CancellationToken ct = default)
        => finalizations.GetBlockedAsync(registerId, limit, ct);

    public Task<IReadOnlyList<OperationalRegisterFinalization>> GetDirtyFinalizationsAcrossAllAsync(
        int limit = 100,
        CancellationToken ct = default)
        => finalizations.GetDirtyAcrossAllAsync(limit, ct);

    public Task<IReadOnlyList<OperationalRegisterFinalization>> GetBlockedFinalizationsAcrossAllAsync(
        int limit = 100,
        CancellationToken ct = default)
        => finalizations.GetBlockedAcrossAllAsync(limit, ct);
}
