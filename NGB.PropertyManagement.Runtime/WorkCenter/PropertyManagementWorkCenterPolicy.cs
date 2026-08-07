using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Core.Documents.Actions;
using NGB.PropertyManagement.Documents;

namespace NGB.PropertyManagement.Runtime.WorkCenter;

public sealed class PropertyManagementWorkCenterPolicy(
    IPropertyManagementDocumentReaders typedDocuments,
    IReceivablePaymentWorkCenterSynchronizer synchronizer)
    : IWorkCenterEventPolicy
{
    public string EventType => StandardDocumentActionCodes.DocumentActionCompletedType;

    public async Task HandleAsync(WorkCenterEventContext context, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(context.Event.PayloadJson);
        var data = json.RootElement.GetProperty("data");
        var documentId = data.GetProperty(StandardDocumentActionCodes.DocumentIdKey).GetGuid();
        var documentType = data.GetProperty(StandardDocumentActionCodes.DocumentType).GetString();
        var actionCode = data.GetProperty(StandardDocumentActionCodes.DocumentActionCode).GetString()?.Trim().ToLowerInvariant();

        if (string.Equals(documentType, PropertyManagementCodes.ReceivablePayment, StringComparison.OrdinalIgnoreCase))
        {
            if (actionCode == StandardDocumentActionCodes.UnpostValue)
            {
                await synchronizer.CancelAsync(documentId, ct);
                return;
            }

            if (actionCode is StandardDocumentActionCodes.PostValue or StandardDocumentActionCodes.RepostValue)
            {
                await synchronizer.SynchronizeAsync(
                    documentId,
                    context.Event.CorrelationId,
                    context.Event.EventId,
                    ct);
            }

            return;
        }

        if (string.Equals(documentType, PropertyManagementCodes.ReceivableApply, StringComparison.OrdinalIgnoreCase)
            && actionCode is StandardDocumentActionCodes.PostValue or StandardDocumentActionCodes.RepostValue or StandardDocumentActionCodes.UnpostValue)
        {
            var apply = await typedDocuments.ReadReceivableApplyHeadAsync(documentId, ct);
            await synchronizer.SynchronizeAsync(
                apply.CreditDocumentId,
                context.Event.CorrelationId,
                context.Event.EventId,
                ct);
        }
    }
}
