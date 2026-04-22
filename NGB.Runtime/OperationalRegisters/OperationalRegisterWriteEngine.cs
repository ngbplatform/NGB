using Microsoft.Extensions.Logging;
using NGB.Accounting.PostingState;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Locks;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// Canonical operational register write pipeline:
/// - transaction (optional)
/// - concurrency guards (document + affected months)
/// - idempotency begin (operational_register_write_state)
/// - execute write action
/// - mark affected months as Dirty
/// - mark state completed
/// </summary>
public sealed class OperationalRegisterWriteEngine(
    IUnitOfWork uow,
    IAdvisoryLockManager locks,
    IOperationalRegisterRepository registers,
    IDocumentRepository documents,
    IOperationalRegisterWriteStateRepository writeLog,
    IOperationalRegisterFinalizationRepository finalizations,
    TimeProvider timeProvider,
    ILogger<OperationalRegisterWriteEngine> logger)
    : IOperationalRegisterWriteEngine
{
    public Task<OperationalRegisterWriteResult> ExecuteAsync(
        Guid registerId,
        Guid documentId,
        OperationalRegisterWriteOperation operation,
        IReadOnlyCollection<DateOnly>? affectedPeriods,
        Func<CancellationToken, Task> writeAction,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        documentId.EnsureNonEmpty(nameof(documentId));
        
        if (writeAction is null)
            throw new NgbArgumentRequiredException(nameof(writeAction));

        // Normalize to month-start (DB constraint expects month start; locks also normalize).
        var months = (affectedPeriods ?? [])
            .Select(OperationalRegisterPeriod.MonthStart)
            .Distinct()
            .ToArray();

        return uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                // Fail fast with a clear message (otherwise FK violations come from the DB).
                if (await registers.GetByIdAsync(registerId, innerCt) is null)
                    throw new OperationalRegisterNotFoundException(registerId);

                if (await documents.GetAsync(documentId, innerCt) is null)
                    throw new DocumentNotFoundException(documentId);

                // Concurrency guards must be inside the same transaction as the idempotency state + writes.
                await locks.LockDocumentAsync(documentId, innerCt);

                IReadOnlyList<DateOnly> periodsToInvalidate = [];
                if (months.Length > 0)
                {
                    await locks.LockOperationalRegisterAsync(registerId, innerCt);

                    periodsToInvalidate = await BuildInvalidatePeriodsAsync(registerId, months, innerCt);
                    foreach (var m in periodsToInvalidate)
                    {
                        await locks.LockPeriodAsync(m, AdvisoryLockPeriodScope.OperationalRegister, innerCt);
                    }
                }

                var startedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                var begin = await writeLog.TryBeginAsync(registerId, documentId, operation, startedAtUtc, innerCt);

                if (begin == PostingStateBeginResult.AlreadyCompleted)
                {
                    logger.LogInformation(
                        "Operational register write already completed (idempotent). registerId={RegisterId}, documentId={DocumentId}, operation={Operation}",
                        registerId,
                        documentId,
                        operation);

                    return OperationalRegisterWriteResult.AlreadyCompleted;
                }

                if (begin == PostingStateBeginResult.InProgress)
                    throw new OperationalRegisterWriteAlreadyInProgressException(registerId, documentId, operation.ToString());

                // Execute user-supplied write action (must write movements/projections inside this transaction).
                await writeAction(innerCt);

                // Any successful write makes projections for affected months dirty.
                // For cumulative balances, any change also invalidates all tracked future periods of the same register.
                if (periodsToInvalidate.Count > 0)
                {
                    var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                    foreach (var m in periodsToInvalidate)
                    {
                        await finalizations.MarkDirtyAsync(registerId, m, dirtySinceUtc: nowUtc, nowUtc: nowUtc, innerCt);
                    }
                }

                await writeLog.MarkCompletedAsync(registerId, documentId, operation, timeProvider.GetUtcNow().UtcDateTime, innerCt);

                logger.LogInformation(
                    "Operational register write completed. registerId={RegisterId}, documentId={DocumentId}, operation={Operation}, months={Months}",
                    registerId,
                    documentId,
                    operation,
                    periodsToInvalidate.Count);

                return OperationalRegisterWriteResult.Executed;
            },
            ct);
    }

    private async Task<IReadOnlyList<DateOnly>> BuildInvalidatePeriodsAsync(
        Guid registerId,
        IReadOnlyCollection<DateOnly> affectedMonths,
        CancellationToken ct)
    {
        var chainStart = affectedMonths.Min();
        var tracked = await finalizations.GetTrackedPeriodsOnOrAfterAsync(registerId, chainStart, ct);

        return tracked
            .Concat(affectedMonths)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
    }
}
