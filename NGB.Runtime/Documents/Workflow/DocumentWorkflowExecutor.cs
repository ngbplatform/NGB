using Microsoft.Extensions.Logging;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Diagnostics;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.Workflow;

/// <summary>
/// Default implementation of <see cref="IDocumentWorkflowExecutor"/>.
/// </summary>
public sealed class DocumentWorkflowExecutor(
    IUnitOfWork uow,
    IAdvisoryLockManager locks,
    ILogger<DocumentWorkflowExecutor> logger)
    : IDocumentWorkflowExecutor
{
    public async Task ExecuteAsync(
        string operationName,
        Guid? documentId,
        Func<CancellationToken, Task<bool>> action,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(operationName))
            throw new NgbArgumentRequiredException(nameof(operationName));

        if (action is null)
            throw new NgbArgumentRequiredException(nameof(action));

        // Keep scope keys stable for log search/filter.
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["Operation"] = operationName,
            ["DocumentId"] = documentId
        });

        RuntimeLog.DocumentOperationStarted(logger, operationName);

        var didWork = false;
        try
        {
            await uow.ExecuteInUowTransactionAsync(manageTransaction, async innerCt =>
            {
                if (documentId is not null)
                    await locks.LockDocumentAsync(documentId.Value, innerCt);

                didWork = await action(innerCt);
            }, ct);

            if (didWork)
                RuntimeLog.DocumentOperationCompleted(logger, operationName);
            else
                RuntimeLog.DocumentOperationNoOp(logger, operationName);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is normal control flow for request-scoped operations.
            throw;
        }
        catch (Exception ex)
        {
            // Keep error message stable and operation-driven (no document-specific details).
            logger.LogError(ex, "Document workflow failed: {Operation}.", operationName);

            // Safety net: never leak raw exceptions outside runtime boundaries.
            // Preserve the original exception as InnerException.
            var wrapped = NgbExceptionPolicy.Apply(ex, operationName, new Dictionary<string, object?>
            {
                ["documentId"] = documentId
            });

            throw wrapped;
        }
    }
}
