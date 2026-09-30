using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.Runtime.ReferenceRegisters;

/// <summary>
/// Default implementation of <see cref="IReferenceRegisterAdminMaintenanceService"/>.
/// </summary>
internal sealed class ReferenceRegisterAdminMaintenanceService(
    IUnitOfWork uow,
    IReferenceRegisterRepository registers,
    IReferenceRegisterRecordsStore recordsStore,
    IReferenceRegisterPhysicalSchemaHealthReader healthReader)
    : IReferenceRegisterAdminBatchMaintenanceService
{
    public async Task<ReferenceRegisterPhysicalSchemaHealth?> EnsurePhysicalSchemaByIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        await uow.EnsureConnectionOpenAsync(ct);

        var reg = await registers.GetByIdAsync(registerId, ct);
        if (reg is null)
            return null;

        await uow.ExecuteInUowTransactionAsync(token => recordsStore.EnsureSchemaAsync(reg.RegisterId, token), ct);

        // Re-read after ensure (outside the transaction) to provide the actual current state.
        return await healthReader.GetByRegisterIdAsync(reg.RegisterId, ct);
    }

    public async Task EnsurePhysicalSchemasByIdsAsync(
        IReadOnlyCollection<Guid> registerIds,
        CancellationToken ct = default)
    {
        if (registerIds is null)
            throw new ArgumentNullException(nameof(registerIds));

        var ids = registerIds
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .OrderBy(static id => id)
            .ToArray();

        if (ids.Length == 0)
            return;

        await uow.EnsureConnectionOpenAsync(ct);

        var existing = await registers.GetByIdsAsync(ids, ct);
        if (existing.Count == 0)
            return;

        await uow.ExecuteInUowTransactionAsync(
            async token =>
            {
                foreach (var reg in existing.OrderBy(static item => item.RegisterId))
                {
                    await recordsStore.EnsureSchemaAsync(reg.RegisterId, token);
                }
            },
            ct);
    }

    public async Task<ReferenceRegisterPhysicalSchemaHealthReport> EnsurePhysicalSchemaForAllAsync(
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        var before = await healthReader.GetReportAsync(ct);
        var unhealthy = before.Items.Where(static item => !item.IsOk).ToArray();
        if (unhealthy.Length == 0)
            return before;

        // Healthy schemas are intentionally skipped: EnsureSchema performs DDL
        // discovery and locking even when there is nothing to repair.
        foreach (var item in unhealthy)
        {
            var registerId = item.Register.RegisterId;

            await uow.ExecuteInUowTransactionAsync(token => recordsStore.EnsureSchemaAsync(registerId, token), ct);
        }

        return await healthReader.GetReportAsync(ct);
    }
}
