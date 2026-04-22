using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

/// <summary>
/// Validation errors for the receivables batch apply endpoint.
/// Client-actionable (HTTP 400 via GlobalErrorHandling).
/// </summary>
public sealed class ReceivablesApplyBatchValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static ReceivablesApplyBatchValidationException AppliesMustNotBeEmpty()
        => new(
            "At least one application is required.",
            errorCode: "pm.validation.receivables.apply_batch.applies_empty",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["applies"] = ["At least one application is required."]
                }
            });

    public static ReceivablesApplyBatchValidationException AppliesTooLarge(int count, int max)
        => new(
            $"You can apply at most {max} items at a time.",
            errorCode: "pm.validation.receivables.apply_batch.applies_too_large",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["count"] = count,
                ["max"] = max,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["applies"] = [$"You can apply at most {max} items at a time."]
                }
            });

    public static ReceivablesApplyBatchValidationException PayloadFieldMissing(string field)
    {
        var message = field == "fields"
            ? "Application details are required."
            : $"{PropertyManagementValidationLabels.Label(field)} is required.";
        return new(
            message,
            errorCode: "pm.validation.receivables.apply_batch.payload_field_missing",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["field"] = field,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [field] = [message]
                }
            });
    }

    public static ReceivablesApplyBatchValidationException PayloadFieldInvalid(string field, string error)
    {
        var message = field switch
        {
            "credit_document_id" => "Select a valid Credit Source.",
            "charge_document_id" => "Select a valid Charge.",
            "applied_on_utc" => "Enter a valid date for Applied On.",
            "amount" => "Enter a valid number for Amount.",
            _ => $"Enter a valid value for {PropertyManagementValidationLabels.Label(field)}."
        };

        return new(
            message,
            errorCode: "pm.validation.receivables.apply_batch.payload_field_invalid",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["field"] = field,
                ["rawError"] = error,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [field] = [message]
                }
            });
    }

    public static ReceivablesApplyBatchValidationException ApplyNotFound(Guid applyId)
        => new(
            "Selected application was not found.",
            errorCode: "pm.validation.receivables.apply_batch.apply_not_found",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["applyId"] = applyId,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["applyId"] = ["Selected application was not found."]
                }
            });

    public static ReceivablesApplyBatchValidationException ApplyNotDraft(Guid applyId)
        => new(
            "Selected application must be in Draft.",
            errorCode: "pm.validation.receivables.apply_batch.apply_not_draft",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["applyId"] = applyId,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["applyId"] = ["Selected application must be in Draft."]
                }
            });

    public static ReceivablesApplyBatchValidationException ApplyWrongType(Guid applyId, string actualType)
        => new(
            "Selected document is not a receivable application.",
            errorCode: "pm.validation.receivables.apply_batch.apply_wrong_type",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["applyId"] = applyId,
                ["actualType"] = actualType,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["applyId"] = ["Selected document is not a receivable application."]
                }
            });
}
