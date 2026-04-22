using Microsoft.Extensions.Logging;
using NGB.Accounting.PostingState;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.UnitOfWork;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.ReferenceRegisters;

/// <summary>
/// Canonical reference register write pipeline:
/// - transaction (optional)
/// - concurrency guards (document)
/// - idempotency begin (reference_register_write_state)
/// - execute write action
/// - mark state completed
/// </summary>
public sealed class ReferenceRegisterWriteEngine(
    IUnitOfWork uow,
    IAdvisoryLockManager locks,
    IReferenceRegisterRepository registers,
    IDocumentRepository documents,
    IReferenceRegisterWriteStateRepository writeLog,
    ILogger<ReferenceRegisterWriteEngine> logger,
    TimeProvider timeProvider)
    : IReferenceRegisterWriteEngine
{
    public Task<ReferenceRegisterWriteResult> ExecuteAsync(
        Guid registerId,
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        Func<CancellationToken, Task> writeAction,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        documentId.EnsureNonEmpty(nameof(documentId));
        
        if (writeAction is null)
            throw new NgbArgumentRequiredException(nameof(writeAction));

        return uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                // Fail fast with clear messages (otherwise FK violations come from the DB).
                if (await registers.GetByIdAsync(registerId, innerCt) is null)
                    throw new ReferenceRegisterNotFoundException(registerId);

                if (await documents.GetAsync(documentId, innerCt) is null)
                    throw new ReferenceRegisterDocumentNotFoundException(documentId);

                // Concurrency guards must be inside the same transaction as the idempotency state + writes.
                await locks.LockDocumentAsync(documentId, innerCt);

                var startedAtUtc = timeProvider.GetUtcNowDateTime();
                var begin = await writeLog.TryBeginAsync(registerId, documentId, operation, startedAtUtc, innerCt);

                if (begin == PostingStateBeginResult.AlreadyCompleted)
                {
                    logger.LogInformation(
                        "Reference register write already completed (idempotent). registerId={RegisterId}, documentId={DocumentId}, operation={Operation}",
                        registerId,
                        documentId,
                        operation);

                    return ReferenceRegisterWriteResult.AlreadyCompleted;
                }

                if (begin == PostingStateBeginResult.InProgress)
                    throw new ReferenceRegisterWriteAlreadyInProgressException(registerId, documentId, operation.ToString());

                await writeAction(innerCt);

                await writeLog.MarkCompletedAsync(registerId, documentId, operation, timeProvider.GetUtcNowDateTime(), innerCt);

                logger.LogInformation(
                    "Reference register write completed. registerId={RegisterId}, documentId={DocumentId}, operation={Operation}",
                    registerId,
                    documentId,
                    operation);

                return ReferenceRegisterWriteResult.Executed;
            },
            ct);
    }
}
