using NGB.Core.Documents;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class PayableApplyValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static PayableApplyValidationException AmountMustBePositive(decimal amount)
        => new(
            "Apply amount must be positive.",
            "pm.validation.payables.apply.amount_must_be_positive",
            BuildAmountErrors("Amount must be positive.", amount));

    public static PayableApplyValidationException CreditSourceRequired()
        => new(
            "Credit Source is required.",
            "pm.validation.payables.apply.credit_required",
            BuildFieldErrors("credit_document_id", "Credit Source is required."));

    public static PayableApplyValidationException ChargeRequired()
        => new(
            "Charge is required.",
            "pm.validation.payables.apply.charge_required",
            BuildFieldErrors("charge_document_id", "Charge is required."));

    public static PayableApplyValidationException CreditSourceAndChargeMustDiffer(
        Guid creditDocumentId,
        Guid chargeDocumentId)
        => new(
            "Credit source and charge must refer to different documents.",
            "pm.validation.payables.apply.credit_charge_same",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["creditDocumentId"] = creditDocumentId,
                ["chargeDocumentId"] = chargeDocumentId,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["credit_document_id"] = ["Credit source and charge must be different."],
                    ["charge_document_id"] = ["Credit source and charge must be different."]
                }
            });

    public static PayableApplyValidationException CreditSourceNotFound(Guid creditDocumentId)
        => new(
            "Selected credit source was not found.",
            "pm.validation.payables.apply.credit_not_found",
            BuildFieldErrors(
                "credit_document_id",
                "Selected credit source was not found.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["creditDocumentId"] = creditDocumentId
                }));

    public static PayableApplyValidationException CreditSourceWrongType(Guid creditDocumentId, string actualType)
        => new(
            "Selected document is not a payable credit source.",
            "pm.validation.payables.apply.credit_wrong_type",
            BuildFieldErrors(
                "credit_document_id",
                "Selected document is not a payable credit source.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["creditDocumentId"] = creditDocumentId,
                    ["actualType"] = actualType
                }));

    public static PayableApplyValidationException CreditSourceMustBePosted(Guid creditDocumentId, DocumentStatus status)
        => new(
            "Selected credit source must be posted before it can be applied.",
            "pm.validation.payables.apply.credit_must_be_posted",
            BuildFieldErrors(
                "credit_document_id",
                "Selected credit source must be posted before it can be applied.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["creditDocumentId"] = creditDocumentId,
                    ["status"] = status.ToString()
                }));

    public static PayableApplyValidationException ChargeNotFound(Guid chargeDocumentId)
        => new(
            "Selected charge was not found.",
            "pm.validation.payables.apply.charge_not_found",
            BuildFieldErrors(
                "charge_document_id",
                "Selected charge was not found.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["chargeDocumentId"] = chargeDocumentId
                }));

    public static PayableApplyValidationException ChargeWrongType(Guid chargeDocumentId, string actualType)
        => new(
            "Selected document is not a payable charge.",
            "pm.validation.payables.apply.charge_wrong_type",
            BuildFieldErrors(
                "charge_document_id",
                "Selected document is not a payable charge.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["chargeDocumentId"] = chargeDocumentId,
                    ["actualType"] = actualType
                }));

    public static PayableApplyValidationException ChargeMustBePosted(Guid chargeDocumentId, DocumentStatus status)
        => new(
            "Selected charge must be posted before it can be applied.",
            "pm.validation.payables.apply.charge_must_be_posted",
            BuildFieldErrors(
                "charge_document_id",
                "Selected charge must be posted before it can be applied.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["chargeDocumentId"] = chargeDocumentId,
                    ["status"] = status.ToString()
                }));

    public static PayableApplyValidationException PartyPropertyMismatch(Guid creditDocumentId, Guid chargeDocumentId)
        => new(
            "Credit source and charge must belong to the same vendor/property.",
            "pm.validation.payables.apply.party_property_mismatch",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["creditDocumentId"] = creditDocumentId,
                ["chargeDocumentId"] = chargeDocumentId,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["credit_document_id"] = ["Credit source and charge must belong to the same vendor/property."],
                    ["charge_document_id"] = ["Credit source and charge must belong to the same vendor/property."]
                }
            });

    public static PayableApplyValidationException OverApplyCharge(
        Guid chargeDocumentId,
        decimal requested,
        decimal outstanding)
        => new(
            $"Cannot apply {requested} because charge outstanding is {outstanding}.",
            "pm.validation.payables.apply.over_apply_charge",
            BuildAmountErrors($"Outstanding is {outstanding}.", requested, new Dictionary<string, object?>
            {
                ["chargeDocumentId"] = chargeDocumentId,
                ["outstanding"] = outstanding
            }));

    public static PayableApplyValidationException InsufficientCredit(
        Guid creditDocumentId,
        decimal requested,
        decimal availableCredit)
        => new(
            $"Cannot apply {requested} because available credit is {availableCredit}.",
            "pm.validation.payables.apply.insufficient_credit",
            BuildAmountErrors($"Available credit is {availableCredit}.", requested, new Dictionary<string, object?>
            {
                ["creditDocumentId"] = creditDocumentId,
                ["availableCredit"] = availableCredit
            }));

    private static IReadOnlyDictionary<string, object?> BuildFieldErrors(
        string field,
        string error,
        Dictionary<string, object?>? extra = null)
    {
        var ctx = extra ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        ctx["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = [error]
        };
        return ctx;
    }

    private static IReadOnlyDictionary<string, object?> BuildAmountErrors(
        string error,
        decimal amount,
        Dictionary<string, object?>? extra = null)
    {
        var ctx = extra ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        ctx["amount"] = amount;
        ctx["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["amount"] = [error]
        };
        return ctx;
    }
}
