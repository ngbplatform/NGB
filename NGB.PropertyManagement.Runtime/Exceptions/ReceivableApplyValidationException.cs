using NGB.Core.Documents;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

/// <summary>
/// Domain validation for pm.receivable_apply posting (open-item receivables).
///
/// This is a client-actionable validation error (HTTP 400 via GlobalErrorHandling).
/// </summary>
public sealed class ReceivableApplyValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static ReceivableApplyValidationException AmountMustBePositive(decimal amount)
        => new(
            $"Apply amount must be positive. Actual: {amount}.",
            errorCode: "pm.validation.receivables.apply.amount_must_be_positive",
            context: BuildAmountErrors("Amount must be positive.", amount));

    public static ReceivableApplyValidationException CreditSourceRequired()
        => new(
            "Credit Source is required.",
            errorCode: "pm.validation.receivables.apply.credit_required",
            context: BuildFieldErrors("credit_document_id", "Credit Source is required."));

    public static ReceivableApplyValidationException ChargeRequired()
        => new(
            "Charge is required.",
            errorCode: "pm.validation.receivables.apply.charge_required",
            context: BuildFieldErrors("charge_document_id", "Charge is required."));

    public static ReceivableApplyValidationException PaymentAndChargeMustMatch(
        Guid creditDocumentId,
        Guid chargeDocumentId)
        => new(
            "Credit source and charge must refer to different documents.",
            errorCode: "pm.validation.receivables.apply.payment_charge_same",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["creditDocumentId"] = creditDocumentId,
                ["chargeDocumentId"] = chargeDocumentId,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["credit_document_id"] = ["Credit source and charge must be different."],
                    ["charge_document_id"] = ["Credit source and charge must be different."]
                }
            });

    public static ReceivableApplyValidationException PaymentNotFound(Guid creditDocumentId)
        => new(
            "Selected credit source was not found.",
            errorCode: "pm.validation.receivables.apply.payment_not_found",
            context: BuildFieldErrors(
                "credit_document_id",
                "Selected credit source was not found.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["creditDocumentId"] = creditDocumentId
                }));

    public static ReceivableApplyValidationException PaymentWrongType(Guid creditDocumentId, string actualType)
        => new(
            "Selected document is not a receivable credit source.",
            errorCode: "pm.validation.receivables.apply.payment_wrong_type",
            context: BuildFieldErrors(
                "credit_document_id",
                "Selected document is not a receivable credit source.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["creditDocumentId"] = creditDocumentId,
                    ["actualType"] = actualType
                }));

    public static ReceivableApplyValidationException PaymentMustBePosted(Guid creditDocumentId, DocumentStatus status)
        => new(
            "Selected credit source must be posted before it can be applied.",
            errorCode: "pm.validation.receivables.apply.payment_must_be_posted",
            context: BuildFieldErrors(
                "credit_document_id",
                "Selected credit source must be posted before it can be applied.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["creditDocumentId"] = creditDocumentId,
                    ["status"] = status.ToString()
                }));

    public static ReceivableApplyValidationException ChargeNotFound(Guid chargeDocumentId)
        => new(
            "Selected charge was not found.",
            errorCode: "pm.validation.receivables.apply.charge_not_found",
            context: BuildFieldErrors(
                "charge_document_id",
                "Selected charge was not found.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["chargeDocumentId"] = chargeDocumentId
                }));

    public static ReceivableApplyValidationException ChargeWrongType(Guid chargeDocumentId, string actualType)
        => new(
            "Selected document is not an applyable charge document.",
            errorCode: "pm.validation.receivables.apply.charge_wrong_type",
            context: BuildFieldErrors(
                "charge_document_id",
                "Selected document is not an applyable charge document.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["chargeDocumentId"] = chargeDocumentId,
                    ["actualType"] = actualType
                }));

    public static ReceivableApplyValidationException ChargeMustBePosted(Guid chargeDocumentId, DocumentStatus status)
        => new(
            "Selected charge must be posted before it can be applied.",
            errorCode: "pm.validation.receivables.apply.charge_must_be_posted",
            context: BuildFieldErrors(
                "charge_document_id",
                "Selected charge must be posted before it can be applied.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["chargeDocumentId"] = chargeDocumentId,
                    ["status"] = status.ToString()
                }));

    public static ReceivableApplyValidationException PartyPropertyLeaseMismatch(
        Guid creditDocumentId,
        Guid chargeDocumentId)
        => new(
            "Credit source and charge must belong to the same party/property/lease.",
            errorCode: "pm.validation.receivables.apply.party_property_lease_mismatch",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["creditDocumentId"] = creditDocumentId,
                ["chargeDocumentId"] = chargeDocumentId,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["credit_document_id"] = ["Credit source and charge must belong to the same lease."],
                    ["charge_document_id"] = ["Credit source and charge must belong to the same lease."]
                }
            });

    public static ReceivableApplyValidationException OverApplyCharge(
        Guid chargeDocumentId,
        decimal requested,
        decimal outstanding)
        => new(
            $"Cannot apply {requested} because charge outstanding is {outstanding}.",
            errorCode: "pm.validation.receivables.apply.over_apply_charge",
            context: BuildAmountErrors($"Outstanding is {outstanding}.", requested, new Dictionary<string, object?>
            {
                ["chargeDocumentId"] = chargeDocumentId,
                ["outstanding"] = outstanding
            }));

    public static ReceivableApplyValidationException InsufficientCredit(
        Guid creditDocumentId,
        decimal requested,
        decimal availableCredit)
        => new(
            $"Cannot apply {requested} because available credit is {availableCredit}.",
            errorCode: "pm.validation.receivables.apply.insufficient_credit",
            context: BuildAmountErrors($"Available credit is {availableCredit}.", requested, new Dictionary<string, object?>
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
