using Microsoft.Extensions.Logging;
using NGB.Application.Abstractions.Services;
using NGB.Core.WorkCenter;
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
    IDocumentService documents,
    IReceivablesApplyAvailabilitySource availability,
    IWorkCenterTaskService tasks,
    IWorkCenterRealtimeNotifier realtime,
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
        var payment = await documents.GetByIdAsync(
            PropertyManagementCodes.ReceivablePayment,
            paymentId,
            ct);

        var availabilityResult = await availability.EvaluateAsync(
            PropertyManagementCodes.ReceivablePayment,
            paymentId,
            payment.Status,
            ct);

        var deduplicationKey = DeduplicationKey(paymentId);

        if (!availabilityResult.IsAllowed)
        {
            await tasks.CompleteByDeduplicationKeyAsync(deduplicationKey, ct);
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
                    payment.Display ?? payment.Number ?? "Receivable payment",
                    payment.Number),
                "Apply receivable payment",
                "Open receivables reconciliation and apply the remaining payment amount.",
                WorkCenterPriority.High,
                AssignedUserId: null,
                AssignedRoleCode: PropertyManagementWorkCenterCodes.AccountsReceivableClerkRole,
                DueAtUtc: now.AddDays(3).UtcDateTime,
                PrimaryActionCode: PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation.Value,
                NavigationTargetCode: "pm.receivables.reconciliation",
                NavigationParameters: new Dictionary<string, string?>
                {
                    ["paymentId"] = paymentId.ToString()
                },
                deduplicationKey,
                correlationId,
                causationId,
                MetadataJson: null),
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
        var payment = await documents.GetByIdAsync(
            PropertyManagementCodes.ReceivablePayment,
            paymentId,
            ct);

        var availabilityResult = await availability.EvaluateAsync(
            PropertyManagementCodes.ReceivablePayment,
            paymentId,
            payment.Status,
            ct);

        if (!availabilityResult.IsAllowed)
            await tasks.CompleteByDeduplicationKeyAsync(DeduplicationKey(paymentId), ct);
    }

    public Task CancelAsync(Guid paymentId, CancellationToken ct)
        => tasks.CancelByDeduplicationKeyAsync(DeduplicationKey(paymentId), ct);

    public async Task NotifyChangedAsync(CancellationToken ct)
    {
        try
        {
            await realtime.NotifyChangedAsync(timeProvider.GetUtcNow().UtcTicks, ct);
        }
        catch (Exception ex)
        {
            // HTTP is authoritative; realtime is an invalidation optimization.
            logger.LogWarning(ex, "Work Center realtime invalidation failed after receivables task synchronization.");
        }
    }

    private static string DeduplicationKey(Guid paymentId) => $"pm:receivable-payment:{paymentId:D}:apply";
}
