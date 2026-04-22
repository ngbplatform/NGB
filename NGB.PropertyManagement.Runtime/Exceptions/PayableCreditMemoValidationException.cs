using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class PayableCreditMemoValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static PayableCreditMemoValidationException VendorNotFound(Guid partyId, Guid? documentId = null)
        => new(
            message: "Selected vendor was not found.",
            errorCode: "pm.validation.payable_credit_memo.vendor_not_found",
            context: BuildContext(documentId, partyId: partyId, errors: new Dictionary<string, string[]>
            {
                ["party_id"] = ["Selected vendor was not found."]
            }));

    public static PayableCreditMemoValidationException VendorDeleted(Guid partyId, Guid? documentId = null)
        => new(
            message: "Selected vendor is marked for deletion.",
            errorCode: "pm.validation.payable_credit_memo.vendor_deleted",
            context: BuildContext(documentId, partyId: partyId, errors: new Dictionary<string, string[]>
            {
                ["party_id"] = ["Selected vendor is marked for deletion."]
            }));

    public static PayableCreditMemoValidationException VendorRoleRequired(Guid partyId, Guid? documentId = null)
        => new(
            message: "Selected party must have Vendor role enabled.",
            errorCode: "pm.validation.payable_credit_memo.vendor_required",
            context: BuildContext(documentId, partyId: partyId, errors: new Dictionary<string, string[]>
            {
                ["party_id"] = ["Selected party must have Vendor role enabled."]
            }));

    public static PayableCreditMemoValidationException PropertyNotFound(Guid propertyId, Guid? documentId = null)
        => new(
            message: "Selected property was not found.",
            errorCode: "pm.validation.payable_credit_memo.property_not_found",
            context: BuildContext(documentId, propertyId: propertyId, errors: new Dictionary<string, string[]>
            {
                ["property_id"] = ["Selected property was not found."]
            }));

    public static PayableCreditMemoValidationException PropertyDeleted(Guid propertyId, Guid? documentId = null)
        => new(
            message: "Selected property is marked for deletion.",
            errorCode: "pm.validation.payable_credit_memo.property_deleted",
            context: BuildContext(documentId, propertyId: propertyId, errors: new Dictionary<string, string[]>
            {
                ["property_id"] = ["Selected property is marked for deletion."]
            }));

    public static PayableCreditMemoValidationException ChargeTypeNotFound(Guid chargeTypeId, Guid? documentId = null)
        => new(
            message: "Selected charge type was not found.",
            errorCode: "pm.validation.payable_credit_memo.charge_type_not_found",
            context: BuildContext(documentId, chargeTypeId: chargeTypeId, errors: new Dictionary<string, string[]>
            {
                ["charge_type_id"] = ["Selected charge type was not found."]
            }));

    public static PayableCreditMemoValidationException ChargeTypeDeleted(Guid chargeTypeId, Guid? documentId = null)
        => new(
            message: "Selected charge type is marked for deletion.",
            errorCode: "pm.validation.payable_credit_memo.charge_type_deleted",
            context: BuildContext(documentId, chargeTypeId: chargeTypeId, errors: new Dictionary<string, string[]>
            {
                ["charge_type_id"] = ["Selected charge type is marked for deletion."]
            }));

    public static PayableCreditMemoValidationException AmountMustBePositive(decimal amount, Guid? documentId = null)
        => new(
            message: "Amount must be positive.",
            errorCode: "pm.validation.payable_credit_memo.amount_must_be_positive",
            context: BuildContext(documentId, amount: amount, errors: new Dictionary<string, string[]>
            {
                ["amount"] = ["Amount must be positive."]
            }));

    private static IReadOnlyDictionary<string, object?> BuildContext(
        Guid? documentId = null,
        Guid? partyId = null,
        Guid? propertyId = null,
        Guid? chargeTypeId = null,
        decimal? amount = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var ctx = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (documentId is not null)
            ctx["documentId"] = documentId.Value;
        if (partyId is not null)
            ctx["partyId"] = partyId.Value;
        if (propertyId is not null)
            ctx["propertyId"] = propertyId.Value;
        if (chargeTypeId is not null)
            ctx["chargeTypeId"] = chargeTypeId.Value;
        if (amount is not null)
            ctx["amount"] = amount.Value;
        if (errors is not null)
            ctx["errors"] = errors;
        return ctx;
    }
}
