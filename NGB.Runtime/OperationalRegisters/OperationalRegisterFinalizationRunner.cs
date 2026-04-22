using Microsoft.Extensions.Logging;
using NGB.Core.Locks;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.OperationalRegisters.Projections;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// Default implementation of <see cref="IOperationalRegisterFinalizationRunner"/>.
///
/// The runner is provider-agnostic; it relies on persistence contracts and DB advisory locks.
///
/// Semantics:
/// - Enumerates dirty register-months.
/// - For each month, opens a transaction (if <c>manageTransaction=true</c>), acquires a month lock,
///   invokes a module-provided projector (if any) or the default projector, and then marks the month finalized.
/// - Module projectors always win over the default path.
/// - <c>BlockedNoProjector</c> is kept only as a defensive fallback for misconfigured hosts that do not register
///   the default projector.
/// </summary>
public sealed class OperationalRegisterFinalizationRunner(
    IUnitOfWork uow,
    IAdvisoryLockManager locks,
    IOperationalRegisterRepository registers,
    IOperationalRegisterFinalizationRepository finalizations,
    IOperationalRegisterMovementsReader movements,
    IEnumerable<IOperationalRegisterMonthProjector> projectors,
    IEnumerable<IOperationalRegisterDefaultMonthProjector> defaultProjectors,
    IEnumerable<IOperationalRegisterMonthFinalizer> legacyFinalizers,
    TimeProvider timeProvider,
    ILogger<OperationalRegisterFinalizationRunner> logger)
    : IOperationalRegisterFinalizationRunner
{
    private readonly IReadOnlyDictionary<string, IOperationalRegisterMonthProjector> _projectorsByCodeNorm
        = BuildProjectorMap(projectors, legacyFinalizers);

    private readonly IOperationalRegisterDefaultMonthProjector? _defaultProjector
        = ResolveDefaultProjector(defaultProjectors);

    public async Task<int> FinalizeDirtyAsync(
        int maxItems = 50,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        if (maxItems <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(maxItems), maxItems, "MaxItems must be positive.");

        var dirty = await finalizations.GetDirtyAcrossAllAsync(maxItems, ct);
        if (dirty.Count == 0)
            return 0;

        var finalizedCount = 0;
        foreach (var item in dirty)
        {
            if (await FinalizeOneAsync(item.RegisterId, item.Period, manageTransaction, ct))
                finalizedCount++;
        }

        return finalizedCount;
    }

    public async Task<int> FinalizeRegisterDirtyAsync(
        Guid registerId,
        int maxPeriods = 50,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentOutOfRangeException(nameof(registerId), registerId, "RegisterId must not be empty.");

        if (maxPeriods <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(maxPeriods), maxPeriods, "MaxPeriods must be positive.");

        var dirty = await finalizations.GetDirtyAsync(registerId, maxPeriods, ct);
        if (dirty.Count == 0)
            return 0;

        var finalizedCount = 0;
        foreach (var item in dirty)
        {
            if (await FinalizeOneAsync(item.RegisterId, item.Period, manageTransaction, ct))
                finalizedCount++;
        }

        return finalizedCount;
    }

    private async Task<bool> FinalizeOneAsync(
        Guid registerId,
        DateOnly periodMonth,
        bool manageTransaction,
        CancellationToken ct)
    {
        if (manageTransaction)
            await uow.BeginTransactionAsync(ct);
        else
            uow.EnsureActiveTransaction();

        try
        {
            await locks.LockOperationalRegisterAsync(registerId, ct);

            // Month lock prevents concurrent finalization and movement writes to the same month.
            // Namespace this lock to Operational Registers so accounting posting/closing can proceed concurrently.
            await locks.LockPeriodAsync(periodMonth, AdvisoryLockPeriodScope.OperationalRegister, ct);

            // Under concurrency, the dirty list may contain stale items.
            // Re-check the current status under the month lock/transaction to ensure idempotency.
            var current = await finalizations.GetAsync(registerId, periodMonth, ct);
            if (current is null || current.Status != OperationalRegisterFinalizationStatus.Dirty)
            {
                if (manageTransaction)
                    await uow.CommitAsync(ct);

                return false;
            }

            var reg = await registers.GetByIdAsync(registerId, ct);
            if (reg is null)
                throw new OperationalRegisterNotFoundException(registerId);

            var codeNorm = NormalizeCodeNorm(reg.Code);
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var ctx = new OperationalRegisterMonthProjectionContext(
                RegisterId: registerId,
                RegisterCode: reg.Code,
                RegisterCodeNorm: codeNorm,
                PeriodMonth: periodMonth,
                NowUtc: nowUtc,
                Movements: movements,
                UnitOfWork: uow);

            if (_projectorsByCodeNorm.TryGetValue(codeNorm, out var projector))
            {
                await projector.RebuildMonthAsync(ctx, ct);
            }
            else if (_defaultProjector is not null)
            {
                await _defaultProjector.RebuildMonthAsync(ctx, ct);
            }
            else
            {
                await finalizations.MarkBlockedNoProjectorAsync(
                    registerId,
                    periodMonth,
                    blockedSinceUtc: nowUtc,
                    blockedReason: "no_projector",
                    nowUtc: nowUtc,
                    ct: ct);

                logger.LogWarning(
                    "No operational register projector registered for '{RegisterCode}' (code_norm='{CodeNorm}') and no default projector is available. Month marked BlockedNoProjector to avoid repeated retries. Mark it Dirty again after a projector is installed.",
                    reg.Code,
                    codeNorm);

                if (manageTransaction)
                    await uow.CommitAsync(ct);

                return false;
            }

            await finalizations.MarkFinalizedAsync(registerId, periodMonth, nowUtc, nowUtc, ct);

            if (manageTransaction)
                await uow.CommitAsync(ct);

            return true;
        }
        catch
        {
            if (manageTransaction && uow.HasActiveTransaction)
                await uow.RollbackAsync(ct);
            throw;
        }
    }

    private static IReadOnlyDictionary<string, IOperationalRegisterMonthProjector> BuildProjectorMap(
        IEnumerable<IOperationalRegisterMonthProjector> projectors,
        IEnumerable<IOperationalRegisterMonthFinalizer> legacyFinalizers)
    {
        var map = new Dictionary<string, IOperationalRegisterMonthProjector>(StringComparer.Ordinal);

        // New API: projectors
        foreach (var p in projectors)
        {
            if (string.IsNullOrWhiteSpace(p.RegisterCodeNorm))
                throw new NgbConfigurationViolationException(
                    $"{nameof(IOperationalRegisterMonthProjector)} has empty {nameof(IOperationalRegisterMonthProjector.RegisterCodeNorm)}.");

            var key = NormalizeCodeNorm(p.RegisterCodeNorm);
            if (!map.TryAdd(key, p))
                throw new NgbConfigurationViolationException($"Duplicate operational register projector for code_norm '{key}'.");
        }

        // Legacy API: finalizers (adapted)
        foreach (var f in legacyFinalizers)
        {
            if (string.IsNullOrWhiteSpace(f.RegisterCodeNorm))
                throw new NgbConfigurationViolationException(
                    $"{nameof(IOperationalRegisterMonthFinalizer)} has empty {nameof(IOperationalRegisterMonthFinalizer.RegisterCodeNorm)}.");

            var key = NormalizeCodeNorm(f.RegisterCodeNorm);
            if (!map.TryAdd(key, new LegacyFinalizerProjectorAdapter(f)))
                throw new NgbConfigurationViolationException($"Duplicate operational register projector/finalizer for code_norm '{key}'.");
        }

        return map;
    }

    private static IOperationalRegisterDefaultMonthProjector? ResolveDefaultProjector(
        IEnumerable<IOperationalRegisterDefaultMonthProjector> defaultProjectors)
    {
        var materialized = defaultProjectors.Take(2).ToArray();
        return materialized.Length switch
        {
            0 => null,
            1 => materialized[0],
            _ => throw new NgbConfigurationViolationException("Multiple default operational register projectors are registered.")
        };
    }

    private static string NormalizeCodeNorm(string code)
        => code.Trim().ToLowerInvariant();
}
