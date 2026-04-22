using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class PayablesRequestValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static PayablesRequestValidationException VendorRequired()
        => SingleField("partyId", "Vendor is required.", "pm.validation.payables.vendor_required");

    public static PayablesRequestValidationException PropertyRequired()
        => SingleField("propertyId", "Property is required.", "pm.validation.payables.property_required");

    public static PayablesRequestValidationException ApplyRequired()
        => SingleField("applyId", "Apply is required.", "pm.validation.payables.apply_required");

    public static PayablesRequestValidationException LimitInvalid()
        => SingleField("limit", "Limit must be greater than zero.", "pm.validation.payables.limit_invalid");

    public static PayablesRequestValidationException MonthMustBeMonthStart(string field)
        => SingleField(field, $"{PropertyManagementValidationLabels.Label(field)} must be the first day of a month.", "pm.validation.payables.month_start_required");

    public static PayablesRequestValidationException MonthRangeInvalid()
        => new(
            "As of month must be on or before To month.",
            "pm.validation.payables.month_range_invalid",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["asOfMonth"] = ["As of month must be on or before To month."],
                    ["toMonth"] = ["To month must be on or after As of month."]
                }
            });

    private static PayablesRequestValidationException SingleField(string field, string message, string errorCode)
        => new(message, errorCode, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["field"] = field,
            ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = [message]
            }
        });
}
