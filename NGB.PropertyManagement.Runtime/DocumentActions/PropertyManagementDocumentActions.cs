using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using NGB.Core.Documents.Actions;
using NGB.Definitions.Documents.Actions;
using NGB.Metadata.Documents.Actions;

namespace NGB.PropertyManagement.Runtime.DocumentActions;

public static class PropertyManagementDocumentActionCodes
{
    public static readonly DocumentActionCode OpenReceivablesReconciliation = new("pm.open_receivables_reconciliation");
    public static readonly DocumentActionCode OpenPayablesReconciliation = new("pm.open_payables_reconciliation");
}

public interface IPropertyManagementApplyAvailabilitySource
{
    Task<DocumentActionAvailabilityResult> EvaluateAsync(
        string documentType,
        Guid documentId,
        DocumentStatus status,
        CancellationToken ct);
}

public sealed class PropertyManagementDocumentActionDefinitionsContributor : IDocumentActionDefinitionsContributor
{
    public void Contribute(DocumentActionDefinitionsBuilder builder)
    {
        foreach (var documentType in ReceivableApplyDocumentTypes)
        {
            builder.Add(
                documentType,
                new DocumentActionMetadata(
                    PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation,
                    new DocumentActionPresentation(
                        "Apply",
                        Description: "Apply this document in receivables open items.",
                        Icon: "file-apply"),
                    DocumentActionKind.Primary,
                    DocumentActionExecutionKind.Navigation,
                    Order: 300,
                    Target: new DocumentActionTargetMetadata(
                        "pm.receivables.apply",
                        new Dictionary<string, string?>
                        {
                            ["leaseId"] = "{field:lease_id}",
                            ["focusItemId"] = "{documentId}",
                            ["source"] = "{documentType}",
                            ["openApply"] = "1",
                            ["refresh"] = "1"
                        })),
                availabilityEvaluatorType: typeof(PropertyManagementApplyAvailabilityEvaluator));
        }

        foreach (var documentType in PayableApplyDocumentTypes)
        {
            builder.Add(
                documentType,
                new DocumentActionMetadata(
                    PropertyManagementDocumentActionCodes.OpenPayablesReconciliation,
                    new DocumentActionPresentation(
                        "Apply",
                        Description: "Apply this document in payables open items.",
                        Icon: "file-apply"),
                    DocumentActionKind.Primary,
                    DocumentActionExecutionKind.Navigation,
                    Order: 300,
                    Target: new DocumentActionTargetMetadata(
                        "pm.payables.apply",
                        new Dictionary<string, string?>
                        {
                            ["partyId"] = "{field:party_id}",
                            ["propertyId"] = "{field:property_id}",
                            ["focusItemId"] = "{documentId}",
                            ["source"] = "{documentType}",
                            ["openApply"] = "1",
                            ["refresh"] = "1"
                        })),
                availabilityEvaluatorType: typeof(PropertyManagementApplyAvailabilityEvaluator));
        }
    }

    public static IReadOnlyList<string> ReceivableApplyDocumentTypes { get; } =
    [
        PropertyManagementCodes.ReceivableCharge,
        PropertyManagementCodes.RentCharge,
        PropertyManagementCodes.LateFeeCharge,
        PropertyManagementCodes.ReceivablePayment,
        PropertyManagementCodes.ReceivableCreditMemo
    ];

    public static IReadOnlyList<string> PayableApplyDocumentTypes { get; } =
    [
        PropertyManagementCodes.PayableCharge,
        PropertyManagementCodes.PayablePayment,
        PropertyManagementCodes.PayableCreditMemo
    ];
}

public sealed class PropertyManagementApplyActionContextEnricher(
    string documentTypeCode,
    IPropertyManagementApplyAvailabilitySource availability)
    : IDocumentActionContextEnricher
{
    public string DocumentTypeCode { get; } = documentTypeCode;

    public async Task<IReadOnlyDictionary<string, object?>> LoadFactsAsync(
        DocumentActionContextRequest request,
        CancellationToken ct)
    {
        var result = await availability.EvaluateAsync(
            request.Document.TypeCode,
            request.Document.Id,
            request.DocumentDto.Status,
            ct);

        return new Dictionary<string, object?>
        {
            ["pm.apply.allowed"] = result.IsAllowed,
            ["pm.apply.disabled_reasons"] = result.DisabledReasons
        };
    }
}

public sealed class PropertyManagementApplyAvailabilityEvaluator : IDocumentActionAvailabilityEvaluator
{
    public ValueTask<DocumentActionAvailabilityResult> EvaluateAsync(
        DocumentActionEvaluationContext context,
        CancellationToken ct)
    {
        if (context.Facts.TryGetValue("pm.apply.allowed", out var value) && value is true)
            return ValueTask.FromResult(DocumentActionAvailabilityResult.Allowed);

        var reasons = context.Facts.TryGetValue("pm.apply.disabled_reasons", out var configured)
            && configured is IReadOnlyList<DocumentActionDisabledReasonDto> source
                ? source
                : [new DocumentActionDisabledReasonDto("pm.apply.unavailable", "This document has no remaining amount to apply.")];

        return ValueTask.FromResult(new DocumentActionAvailabilityResult(reasons));
    }
}
