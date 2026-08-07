using Microsoft.Extensions.Logging;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using NGB.Core.Documents.Exceptions;
using NGB.Core.WorkCenter;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Runtime.DocumentActions;
using NGB.PropertyManagement.Runtime.Receivables;

namespace NGB.PropertyManagement.Runtime.WorkCenter;

public interface IReceivablePaymentWorkCenterSynchronizer
{
    Task SynchronizeAsync(Guid paymentId, Guid? correlationId, Guid? causationId, CancellationToken ct);

    Task CompleteIfExhaustedAsync(Guid paymentId, CancellationToken ct);

    Task CancelAsync(Guid paymentId, CancellationToken ct);

    Task NotifyChangedAsync(CancellationToken ct);
}

/// <summary>
/// Keeps a receivable payment's task synchronized with the authoritative
/// open-items balance. Both document-action events and atomic apply workflows
/// use this service, so no UI/API path can leave a stale task behind.
/// </summary>
public sealed class ReceivablePaymentWorkCenterSynchronizer(
    IDocumentRepository documents,
    IReceivablesApplyAvailabilitySource availability,
    IWorkCenterTaskService tasks,
    IWorkCenterRealtimeNotifier realtime,
    IWorkCenterChangeTracker changes,
    TimeProvider timeProvider,
    ILogger<ReceivablePaymentWorkCenterSynchronizer> logger)
    : IReceivablePaymentWorkCenterSynchronizer
{
    public async Task SynchronizeAsync(
        Guid paymentId,
        Guid? correlationId,
        Guid? causationId,
        CancellationToken ct)
    {
        var payment = await GetPaymentAsync(paymentId, ct);

        var availabilityResult = await availability.EvaluateAsync(
            PropertyManagementCodes.ReceivablePayment,
            paymentId,
            (DocumentStatus)payment.Status,
            ct);

        var deduplicationKey = DeduplicationKey(paymentId);

        if (!availabilityResult.IsAllowed)
        {
            await tasks.CompleteByDeduplicationKeyAsync(
                PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask,
                deduplicationKey,
                ct);
            return;
        }

        var now = timeProvider.GetUtcNow();

        await tasks.CreateAsync(
            new CreateWorkCenterTaskRequest(
                PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask,
                new WorkCenterSourceReference(
                    "document",
                    PropertyManagementCodes.ReceivablePayment,
                    paymentId,
                    payment.Number ?? "Receivable payment",
                    payment.Number),
                "Apply receivable payment",
                "Open receivables reconciliation and apply the remaining payment amount.",
                WorkCenterPriority.High,
                AssignedUserId: null,
                AssignedRoleCode: PropertyManagementWorkCenterCodes.AccountsReceivableClerkRole,
                DueAtUtc: now.AddDays(3).UtcDateTime,
                PrimaryActionCode: PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation,
                Target: new DocumentActionTargetDto(
                    "pm.receivables.reconciliation",
                    new Dictionary<string, string?>
                    {
                        ["paymentId"] = paymentId.ToString()
                    }),
                deduplicationKey,
                correlationId,
                causationId),
            ct);
    }

    /// <summary>
    /// Completion-only path used by apply workflows. A partial allocation keeps
    /// the existing task untouched; a full allocation completes it. This avoids
    /// making financial posting depend on role resolution when there is no task
    /// state transition to persist.
    /// </summary>
    public async Task CompleteIfExhaustedAsync(Guid paymentId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(paymentId, ct);

        var availabilityResult = await availability.EvaluateAsync(
            PropertyManagementCodes.ReceivablePayment,
            paymentId,
            (DocumentStatus)payment.Status,
            ct);

        if (!availabilityResult.IsAllowed)
            await tasks.CompleteByDeduplicationKeyAsync(
                PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask,
                DeduplicationKey(paymentId),
                ct);
    }

    public Task CancelAsync(Guid paymentId, CancellationToken ct)
        => tasks.CancelByDeduplicationKeyAsync(
            PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask,
            DeduplicationKey(paymentId),
            ct);

    public async Task NotifyChangedAsync(CancellationToken ct)
    {
        try
        {
            var affectedUsers = changes.Drain();
            if (affectedUsers.Count > 0)
                await realtime.NotifyUsersChangedAsync(timeProvider.GetUtcNow().UtcTicks, affectedUsers, ct);
        }
        catch (Exception ex)
        {
            // HTTP is authoritative; realtime is an invalidation optimization.
            logger.LogWarning(ex, "Work Center realtime invalidation failed after receivables task synchronization.");
        }
    }

    private async Task<NGB.Core.Documents.DocumentRecord> GetPaymentAsync(Guid paymentId, CancellationToken ct)
    {
        var payment = await documents.GetAsync(paymentId, ct)
            ?? throw new DocumentNotFoundException(paymentId);

        if (!string.Equals(
                payment.TypeCode,
                PropertyManagementCodes.ReceivablePayment,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentTypeMismatchException(
                paymentId,
                PropertyManagementCodes.ReceivablePayment,
                payment.TypeCode);
        }

        return payment;
    }

    private static string DeduplicationKey(Guid paymentId) => $"pm:receivable-payment:{paymentId:D}:apply";
}
