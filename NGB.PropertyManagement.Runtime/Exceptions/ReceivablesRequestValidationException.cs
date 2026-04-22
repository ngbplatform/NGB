using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class ReceivablesRequestValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static ReceivablesRequestValidationException LeaseRequired()
        => SingleField(
            field: "leaseId",
            message: "Lease is required.",
            errorCode: "pm.validation.receivables.lease_required");

    public static ReceivablesRequestValidationException PaymentRequired()
        => SingleField(
            field: "creditDocumentId",
            message: "Credit Source is required.",
            errorCode: "pm.validation.receivables.payment_required");

    public static ReceivablesRequestValidationException ApplyRequired()
        => SingleField(
            field: "applyId",
            message: "Apply is required.",
            errorCode: "pm.validation.receivables.apply_required");

    public static ReceivablesRequestValidationException MaxApplicationsInvalid()
        => SingleField(
            field: "maxApplications",
            message: "Max applications must be greater than zero.",
            errorCode: "pm.validation.receivables.max_applications_invalid");

    public static ReceivablesRequestValidationException LimitInvalid()
        => SingleField(
            field: "limit",
            message: "Limit must be greater than zero.",
            errorCode: "pm.validation.receivables.limit_invalid");

    public static ReceivablesRequestValidationException ApplicationsRequired()
        => SingleField(
            field: "applies",
            message: "At least one application is required.",
            errorCode: "pm.validation.receivables.applies_required");

    public static ReceivablesRequestValidationException ApplicationsTooLarge(int count, int max)
        => new(
            message: $"You can apply at most {max} items at a time.",
            errorCode: "pm.validation.receivables.applies_too_large",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["count"] = count,
                ["max"] = max,
                ["field"] = "applies",
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["applies"] = [$"You can apply at most {max} items at a time."]
                }
            });

    public static ReceivablesRequestValidationException ChargeRequired(int? rowIndex = null)
        => SingleField(
            field: rowIndex is null ? "applies[].chargeDocumentId" : $"applies[{rowIndex.Value}].chargeDocumentId",
            message: "Charge is required.",
            errorCode: "pm.validation.receivables.charge_required");

    public static ReceivablesRequestValidationException PositiveApplicationAmountRequired()
        => SingleField(
            field: "applies",
            message: "At least one application amount must be greater than zero.",
            errorCode: "pm.validation.receivables.applies_positive_amount_required");

    public static ReceivablesRequestValidationException MonthMustBeMonthStart(string field)
        => SingleField(
            field: field,
            message: $"{PropertyManagementValidationLabels.Label(field)} must be the first day of a month.",
            errorCode: "pm.validation.receivables.month_start_required");

    public static ReceivablesRequestValidationException MonthRangeInvalid()
        => new(
            message: "As of month must be on or before To month.",
            errorCode: "pm.validation.receivables.month_range_invalid",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["asOfMonth"] = ["As of month must be on or before To month."],
                    ["toMonth"] = ["To month must be on or after As of month."]
                }
            });

    public static ReceivablesRequestValidationException PaymentHasNoAvailableCredit(Guid creditDocumentId)
        => new(
            message: "Selected credit source has no available credit.",
            errorCode: "pm.validation.receivables.payment_no_available_credit",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["creditDocumentId"] = creditDocumentId,
                ["field"] = "creditDocumentId",
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["creditDocumentId"] = ["Selected credit source has no available credit."]
                }
            });

    private static ReceivablesRequestValidationException SingleField(string field, string message, string errorCode)
        => new(
            message,
            errorCode,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["field"] = field,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [field] = [message]
                }
            });
}
