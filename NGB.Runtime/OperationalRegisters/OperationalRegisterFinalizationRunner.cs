using Microsoft.Extensions.DependencyInjection;
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
public sealed class OperationalRegisterFinalizationRunner : IOperationalRegisterFinalizationRunner
{
    private readonly IUnitOfWork _uow;
    private readonly IAdvisoryLockManager _locks;
    private readonly IOperationalRegisterRepository _registers;
    private readonly IOperationalRegisterFinalizationRepository _finalizations;
    private readonly IOperationalRegisterMovementsReader _movements;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OperationalRegisterFinalizationRunner> _logger;
    private readonly IOperationalRegisterFinalizationPartitionProcessorFactory? _partitionProcessorFactory;
    private readonly IReadOnlyDictionary<string, IOperationalRegisterMonthProjector> _projectorsByCodeNorm;
    private readonly IOperationalRegisterDefaultMonthProjector? _defaultProjector;

    public OperationalRegisterFinalizationRunner(
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
        : this(
            uow,
            locks,
            registers,
            finalizations,
            movements,
            projectors,
            defaultProjectors,
            legacyFinalizers,
            timeProvider,
            logger,
            partitionProcessorFactory: null)
    {
    }

    internal OperationalRegisterFinalizationRunner(
        IUnitOfWork uow,
        IAdvisoryLockManager locks,
        IOperationalRegisterRepository registers,
        IOperationalRegisterFinalizationRepository finalizations,
        IOperationalRegisterMovementsReader movements,
        IEnumerable<IOperationalRegisterMonthProjector> projectors,
        IEnumerable<IOperationalRegisterDefaultMonthProjector> defaultProjectors,
        IEnumerable<IOperationalRegisterMonthFinalizer> legacyFinalizers,
        TimeProvider timeProvider,
        ILogger<OperationalRegisterFinalizationRunner> logger,
        IOperationalRegisterFinalizationPartitionProcessorFactory? partitionProcessorFactory)
    {
        _uow = uow;
        _locks = locks;
        _registers = registers;
        _finalizations = finalizations;
        _movements = movements;
        _timeProvider = timeProvider;
        _logger = logger;
        _partitionProcessorFactory = partitionProcessorFactory;
        _projectorsByCodeNorm = BuildProjectorMap(projectors, legacyFinalizers);
        _defaultProjector = ResolveDefaultProjector(defaultProjectors);
    }

    public async Task<int> FinalizeDirtyAsync(
        int maxItems = OperationalRegisterFinalizationLimits.DefaultProcessingBatchSize,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        EnsureProcessingBatchSize(maxItems, nameof(maxItems));

        var dirty = await _finalizations.GetDirtyAcrossAllAsync(maxItems, ct);
        if (dirty.Count == 0)
            return 0;

        var registerRows = await _registers.GetByIdsAsync(
            dirty.Select(static item => item.RegisterId).Distinct().ToArray(),
            ct);
        var registersById = registerRows.ToDictionary(static item => item.RegisterId);
        var partitions = dirty
            .GroupBy(static item => item.RegisterId)
            .Select(group => new FinalizationPartition(
                registersById.GetValueOrDefault(group.Key),
                group.OrderBy(static item => item.Period).ToArray()))
            .ToArray();

        if (manageTransaction && _partitionProcessorFactory is not null && partitions.Length > 1)
        {
            var finalizedCount = 0;
            await Parallel.ForEachAsync(
                partitions,
                new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = Math.Min(4, partitions.Length)
                },
                async (partition, innerCt) =>
                {
                    var count = await _partitionProcessorFactory.ProcessAsync(
                        partition.Register,
                        partition.Items,
                        innerCt);
                    Interlocked.Add(ref finalizedCount, count);
                });

            return finalizedCount;
        }

        var sequentialCount = 0;
        foreach (var partition in partitions)
        {
            sequentialCount += await FinalizeSelectedAsync(partition.Register, partition.Items, manageTransaction, ct);
        }

        return sequentialCount;
    }

    public async Task<int> FinalizeRegisterDirtyAsync(
        Guid registerId,
        int maxPeriods = OperationalRegisterFinalizationLimits.DefaultProcessingBatchSize,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentOutOfRangeException(nameof(registerId), registerId, "RegisterId must not be empty.");

        EnsureProcessingBatchSize(maxPeriods, nameof(maxPeriods));

        var dirty = await _finalizations.GetDirtyAsync(registerId, maxPeriods, ct);
        if (dirty.Count == 0)
            return 0;

        var register = await _registers.GetByIdAsync(registerId, ct);
        var finalizedCount = 0;

        foreach (var item in dirty)
        {
            if (await FinalizeOneAsync(item.RegisterId, item.Period, register, manageTransaction, ct))
                finalizedCount++;
        }

        return finalizedCount;
    }

    private static void EnsureProcessingBatchSize(int value, string paramName)
    {
        if (value is < 1 or > OperationalRegisterFinalizationLimits.MaxProcessingBatchSize)
        {
            throw new NgbArgumentOutOfRangeException(
                paramName,
                value,
                $"Value must be between 1 and {OperationalRegisterFinalizationLimits.MaxProcessingBatchSize}.");
        }
    }

    internal async Task<int> FinalizeSelectedAsync(
        OperationalRegisterAdminItem? register,
        IReadOnlyList<OperationalRegisterFinalization> items,
        bool manageTransaction,
        CancellationToken ct)
    {
        var finalizedCount = 0;
        foreach (var item in items)
        {
            if (await FinalizeOneAsync(item.RegisterId, item.Period, register, manageTransaction, ct))
                finalizedCount++;
        }

        return finalizedCount;
    }

    private async Task<bool> FinalizeOneAsync(
        Guid registerId,
        DateOnly periodMonth,
        OperationalRegisterAdminItem? register,
        bool manageTransaction,
        CancellationToken ct)
    {
        if (manageTransaction)
            await _uow.BeginTransactionAsync(ct);
        else
            _uow.EnsureActiveTransaction();

        try
        {
            await _locks.LockOperationalRegisterAsync(registerId, ct);

            // Month lock prevents concurrent finalization and movement writes to the same month.
            // Namespace this lock to Operational Registers so accounting posting/closing can proceed concurrently.
            await _locks.LockPeriodAsync(periodMonth, AdvisoryLockPeriodScope.OperationalRegister, ct);

            // Under concurrency, the dirty list may contain stale items.
            // Re-check the current status under the month lock/transaction to ensure idempotency.
            var current = await _finalizations.GetAsync(registerId, periodMonth, ct);
            if (current is null || current.Status != OperationalRegisterFinalizationStatus.Dirty)
            {
                if (manageTransaction)
                    await _uow.CommitAsync(ct);

                return false;
            }

            if (register is null)
                throw new OperationalRegisterNotFoundException(registerId);

            var codeNorm = NormalizeCodeNorm(register.Code);
            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var ctx = new OperationalRegisterMonthProjectionContext(
                RegisterId: registerId,
                RegisterCode: register.Code,
                RegisterCodeNorm: codeNorm,
                PeriodMonth: periodMonth,
                NowUtc: nowUtc,
                Movements: _movements);

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
                await _finalizations.MarkBlockedNoProjectorAsync(
                    registerId,
                    periodMonth,
                    blockedSinceUtc: nowUtc,
                    blockedReason: "no_projector",
                    nowUtc: nowUtc,
                    ct: ct);

                _logger.LogWarning(
                    "No operational register projector registered for '{RegisterCode}' (code_norm='{CodeNorm}') and no default projector is available. Month marked BlockedNoProjector to avoid repeated retries. Mark it Dirty again after a projector is installed.",
                    register.Code,
                    codeNorm);

                if (manageTransaction)
                    await _uow.CommitAsync(ct);

                return false;
            }

            await _finalizations.MarkFinalizedAsync(registerId, periodMonth, nowUtc, nowUtc, ct);

            if (manageTransaction)
                await _uow.CommitAsync(ct);

            return true;
        }
        catch
        {
            if (manageTransaction && _uow.HasActiveTransaction)
                await _uow.RollbackAsync(ct);

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
                throw new NgbConfigurationViolationException($"{nameof(IOperationalRegisterMonthProjector)} has empty {nameof(IOperationalRegisterMonthProjector.RegisterCodeNorm)}.");

            var key = NormalizeCodeNorm(p.RegisterCodeNorm);
            if (!map.TryAdd(key, p))
                throw new NgbConfigurationViolationException($"Duplicate operational register projector for code_norm '{key}'.");
        }

        // Legacy API: finalizers (adapted)
        foreach (var f in legacyFinalizers)
        {
            if (string.IsNullOrWhiteSpace(f.RegisterCodeNorm))
                throw new NgbConfigurationViolationException($"{nameof(IOperationalRegisterMonthFinalizer)} has empty {nameof(IOperationalRegisterMonthFinalizer.RegisterCodeNorm)}.");

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

    private static string NormalizeCodeNorm(string code) => code.Trim().ToLowerInvariant();

    private sealed record FinalizationPartition(
        OperationalRegisterAdminItem? Register,
        IReadOnlyList<OperationalRegisterFinalization> Items);
}

internal interface IOperationalRegisterFinalizationPartitionProcessorFactory
{
    Task<int> ProcessAsync(
        OperationalRegisterAdminItem? register,
        IReadOnlyList<OperationalRegisterFinalization> items,
        CancellationToken ct);
}

internal sealed class OperationalRegisterFinalizationPartitionProcessorFactory(IServiceScopeFactory scopes)
    : IOperationalRegisterFinalizationPartitionProcessorFactory
{
    public async Task<int> ProcessAsync(
        OperationalRegisterAdminItem? register,
        IReadOnlyList<OperationalRegisterFinalization> items,
        CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<OperationalRegisterFinalizationRunner>();
        return await runner.FinalizeSelectedAsync(register, items, manageTransaction: true, ct);
    }
}
