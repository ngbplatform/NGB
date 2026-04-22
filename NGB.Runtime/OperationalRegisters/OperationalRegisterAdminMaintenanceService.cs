using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// Default implementation of <see cref="IOperationalRegisterAdminMaintenanceService"/>.
/// </summary>
public sealed class OperationalRegisterAdminMaintenanceService(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterMovementsStore movements,
    IOperationalRegisterTurnoversStore turnovers,
    IOperationalRegisterBalancesStore balances,
    IOperationalRegisterPhysicalSchemaHealthReader health,
    IOperationalRegisterFinalizationService finalizations,
    IOperationalRegisterFinalizationRunner finalizationRunner)
    : IOperationalRegisterAdminMaintenanceService
{
    public async Task<OperationalRegisterPhysicalSchemaHealth?> EnsurePhysicalSchemaByIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        await uow.EnsureConnectionOpenAsync(ct);

        // Fail-soft for admin UX: missing register => null.
        var reg = await registers.GetByIdAsync(registerId, ct);
        if (reg is null)
            return null;

        await uow.ExecuteInUowTransactionAsync(
            async token =>
            {
                await movements.EnsureSchemaAsync(registerId, token);
                await turnovers.EnsureSchemaAsync(registerId, token);
                await balances.EnsureSchemaAsync(registerId, token);
            },
            ct);

        // Re-read after ensure (outside the transaction) to provide the actual current state.
        return await health.GetByRegisterIdAsync(registerId, ct);
    }

    public async Task<OperationalRegisterPhysicalSchemaHealthReport> EnsurePhysicalSchemaForAllAsync(
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        var all = await registers.GetAllAsync(ct);
        if (all.Count == 0)
            return await health.GetReportAsync(ct);

        // Ensure each register in its own transaction to keep transactions small.
        foreach (var r in all)
        {
            var registerId = r.RegisterId;

            await uow.ExecuteInUowTransactionAsync(
                async token =>
                {
                    await movements.EnsureSchemaAsync(registerId, token);
                    await turnovers.EnsureSchemaAsync(registerId, token);
                    await balances.EnsureSchemaAsync(registerId, token);
                },
                ct);
        }

        return await health.GetReportAsync(ct);
    }

    public async Task MarkFinalizationDirtyAsync(
        Guid registerId,
        DateOnly periodMonth,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentOutOfRangeException(nameof(registerId), registerId, "RegisterId must not be empty.");

        if (periodMonth.Day != 1)
            throw new NgbArgumentOutOfRangeException(nameof(periodMonth), periodMonth, "Period must be a month start (day=1).");

        await finalizations.MarkDirtyAsync(registerId, periodMonth, manageTransaction: true, ct);
    }

    public Task<int> FinalizeDirtyAsync(int maxItems = 50, CancellationToken ct = default)
        => finalizationRunner.FinalizeDirtyAsync(maxItems, manageTransaction: true, ct);

    public Task<int> FinalizeRegisterDirtyAsync(Guid registerId, int maxPeriods = 50, CancellationToken ct = default)
        => finalizationRunner.FinalizeRegisterDirtyAsync(registerId, maxPeriods, manageTransaction: true, ct);
}
