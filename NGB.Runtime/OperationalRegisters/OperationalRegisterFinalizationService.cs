using Microsoft.Extensions.Logging;
using NGB.Core.Locks;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.Runtime.OperationalRegisters;

public sealed class OperationalRegisterFinalizationService(
    IUnitOfWork uow,
    IAdvisoryLockManager locks,
    IOperationalRegisterRepository registers,
    IOperationalRegisterFinalizationRepository finalizations,
    TimeProvider timeProvider,
    ILogger<OperationalRegisterFinalizationService> logger)
    : IOperationalRegisterFinalizationService
{
    public Task<OperationalRegisterFinalization?> GetAsync(Guid registerId, DateOnly period, CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        return finalizations.GetAsync(registerId, OperationalRegisterPeriod.MonthStart(period), ct);
    }

    public Task MarkDirtyAsync(Guid registerId, DateOnly period, bool manageTransaction = true, CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        var month = OperationalRegisterPeriod.MonthStart(period);

        return uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                if (await registers.GetByIdAsync(registerId, innerCt) is null)
                    throw new OperationalRegisterNotFoundException(registerId);

                await locks.LockOperationalRegisterAsync(registerId, innerCt);

                var periods = await BuildInvalidatePeriodsAsync(registerId, month, innerCt);
                foreach (var p in periods)
                {
                    await locks.LockPeriodAsync(p, AdvisoryLockPeriodScope.OperationalRegister, innerCt);
                }

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                foreach (var p in periods)
                {
                    await finalizations.MarkDirtyAsync(registerId, p, dirtySinceUtc: nowUtc, nowUtc: nowUtc, innerCt);
                }

                logger.LogInformation(
                    "Marked operational register periods Dirty. registerId={RegisterId}, period={Period}, affectedPeriods={AffectedPeriods}",
                    registerId,
                    month,
                    periods.Count);
            },
            ct);
    }

    public Task MarkFinalizedAsync(Guid registerId, DateOnly period, bool manageTransaction = true, CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        var month = OperationalRegisterPeriod.MonthStart(period);

        return uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                if (await registers.GetByIdAsync(registerId, innerCt) is null)
                    throw new OperationalRegisterNotFoundException(registerId);

                await locks.LockOperationalRegisterAsync(registerId, innerCt);
                await locks.LockPeriodAsync(month, AdvisoryLockPeriodScope.OperationalRegister, innerCt);

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                await finalizations.MarkFinalizedAsync(registerId, month, finalizedAtUtc: nowUtc, nowUtc: nowUtc, innerCt);

                logger.LogInformation("Marked operational register period Finalized. registerId={RegisterId}, period={Period}", registerId, month);
            },
            ct);
    }

    private async Task<IReadOnlyList<DateOnly>> BuildInvalidatePeriodsAsync(
        Guid registerId,
        DateOnly month,
        CancellationToken ct)
    {
        var tracked = await finalizations.GetTrackedPeriodsOnOrAfterAsync(registerId, month, ct);
        var periods = tracked
            .Append(month)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        return periods;
    }
}
