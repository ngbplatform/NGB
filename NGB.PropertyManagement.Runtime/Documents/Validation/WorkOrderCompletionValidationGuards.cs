using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

internal static class WorkOrderCompletionValidationGuards
{
    public static async Task ValidateWorkOrderAsync(
        Guid workOrderId,
        Guid? documentId,
        IDocumentRepository documents,
        CancellationToken ct)
    {
        var workOrder = await documents.GetAsync(workOrderId, ct);
        if (workOrder is null
            || !string.Equals(workOrder.TypeCode, PropertyManagementCodes.WorkOrder, StringComparison.OrdinalIgnoreCase))
        {
            throw WorkOrderCompletionValidationException.WorkOrderNotFound(workOrderId, documentId);
        }

        if (workOrder.Status != DocumentStatus.Posted)
            throw WorkOrderCompletionValidationException.WorkOrderMustBePosted(workOrderId, documentId, workOrder.Status);
    }

    public static async Task EnsureNoOtherPostedCompletionAsync(
        Guid workOrderId,
        Guid? excludeDocumentId,
        Guid? documentId,
        IPropertyManagementDocumentReaders readers,
        CancellationToken ct)
    {
        if (await readers.ExistsOtherPostedWorkOrderCompletionAsync(workOrderId, excludeDocumentId, ct))
            throw WorkOrderCompletionValidationException.WorkOrderAlreadyCompleted(workOrderId, documentId);
    }

    public static string NormalizeOutcomeOrThrow(string? value, Guid? documentId = null)
    {
        var normalized = NormalizeOutcome(value);
        if (normalized is null)
            throw WorkOrderCompletionValidationException.OutcomeInvalid(value, documentId);

        return normalized;
    }

    public static string? NormalizeOutcome(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        var normalizedKey = trimmed.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        return normalizedKey switch
        {
            "COMPLETED" => "Completed",
            "CANCELLED" => "Cancelled",
            "UNABLETOCOMPLETE" => "UnableToComplete",
            "UNABLE_TO_COMPLETE" => "UnableToComplete",
            "UNABLE-TO-COMPLETE" => "UnableToComplete",
            _ => null
        };
    }
}
