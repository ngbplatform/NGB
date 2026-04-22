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
    : IReferenceRegisterAdminMaintenanceService
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

    public async Task<ReferenceRegisterPhysicalSchemaHealthReport> EnsurePhysicalSchemaForAllAsync(
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        var list = await registers.GetAllAsync(ct);
        if (list.Count == 0)
            return new ReferenceRegisterPhysicalSchemaHealthReport([]);

        // Ensure each register in its own transaction to keep transactions small.
        foreach (var r in list)
        {
            var registerId = r.RegisterId;

            await uow.ExecuteInUowTransactionAsync(
                token => recordsStore.EnsureSchemaAsync(registerId, token),
                ct);
        }

        return await healthReader.GetReportAsync(ct);
    }
}
