using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class PayablesApplyBatchValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static PayablesApplyBatchValidationException AppliesMustNotBeEmpty()
        => new(
            "At least one application is required.",
            "pm.validation.payables.apply_batch.applies_empty",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["applies"] = ["At least one application is required."]
                }
            });

    public static PayablesApplyBatchValidationException AppliesTooLarge(int count, int max)
        => new(
            $"You can apply at most {max} items at a time.",
            "pm.validation.payables.apply_batch.applies_too_large",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["count"] = count,
                ["max"] = max,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["applies"] = [$"You can apply at most {max} items at a time."]
                }
            });

    public static PayablesApplyBatchValidationException PayloadFieldMissing(string field)
    {
        var message = field == "fields"
            ? "Application details are required."
            : $"{PropertyManagementValidationLabels.Label(field)} is required.";

        return new(
            message,
            "pm.validation.payables.apply_batch.payload_field_missing",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["field"] = field,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [field] = [message]
                }
            });
    }

    public static PayablesApplyBatchValidationException PayloadFieldInvalid(string field, string error)
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
            "pm.validation.payables.apply_batch.payload_field_invalid",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["field"] = field,
                ["rawError"] = error,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [field] = [message]
                }
            });
    }

    public static PayablesApplyBatchValidationException ApplyNotFound(Guid applyId)
        => new(
            "Selected application was not found.",
            "pm.validation.payables.apply_batch.apply_not_found",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["applyId"] = applyId,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["applyId"] = ["Selected application was not found."]
                }
            });

    public static PayablesApplyBatchValidationException ApplyNotDraft(Guid applyId)
        => new(
            "Selected application must be in Draft.",
            "pm.validation.payables.apply_batch.apply_not_draft",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["applyId"] = applyId,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["applyId"] = ["Selected application must be in Draft."]
                }
            });

    public static PayablesApplyBatchValidationException ApplyWrongType(Guid applyId, string actualType)
        => new(
            "Selected document is not a payable application.",
            "pm.validation.payables.apply_batch.apply_wrong_type",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["applyId"] = applyId,
                ["actualType"] = actualType,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["applyId"] = ["Selected document is not a payable application."]
                }
            });
}
