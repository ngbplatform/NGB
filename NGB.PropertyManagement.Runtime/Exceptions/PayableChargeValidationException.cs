using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class PayableChargeValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static PayableChargeValidationException VendorNotFound(Guid partyId, Guid? documentId = null)
        => new(
            message: "Selected vendor was not found.",
            errorCode: "pm.validation.payable_charge.vendor_not_found",
            context: BuildContext(documentId, partyId: partyId, errors: new Dictionary<string, string[]>
            {
                ["party_id"] = ["Selected vendor was not found."]
            }));

    public static PayableChargeValidationException VendorDeleted(Guid partyId, Guid? documentId = null)
        => new(
            message: "Selected vendor is marked for deletion.",
            errorCode: "pm.validation.payable_charge.vendor_deleted",
            context: BuildContext(documentId, partyId: partyId, errors: new Dictionary<string, string[]>
            {
                ["party_id"] = ["Selected vendor is marked for deletion."]
            }));

    public static PayableChargeValidationException VendorRoleRequired(Guid partyId, Guid? documentId = null)
        => new(
            message: "Selected party must have Vendor role enabled.",
            errorCode: "pm.validation.payable_charge.vendor_required",
            context: BuildContext(documentId, partyId: partyId, errors: new Dictionary<string, string[]>
            {
                ["party_id"] = ["Selected party must have Vendor role enabled."]
            }));

    public static PayableChargeValidationException PropertyNotFound(Guid propertyId, Guid? documentId = null)
        => new(
            message: "Selected property was not found.",
            errorCode: "pm.validation.payable_charge.property_not_found",
            context: BuildContext(documentId, propertyId: propertyId, errors: new Dictionary<string, string[]>
            {
                ["property_id"] = ["Selected property was not found."]
            }));

    public static PayableChargeValidationException PropertyDeleted(Guid propertyId, Guid? documentId = null)
        => new(
            message: "Selected property is marked for deletion.",
            errorCode: "pm.validation.payable_charge.property_deleted",
            context: BuildContext(documentId, propertyId: propertyId, errors: new Dictionary<string, string[]>
            {
                ["property_id"] = ["Selected property is marked for deletion."]
            }));

    public static PayableChargeValidationException ChargeTypeNotFound(Guid chargeTypeId, Guid? documentId = null)
        => new(
            message: "Selected charge type was not found.",
            errorCode: "pm.validation.payable_charge.charge_type_not_found",
            context: BuildContext(documentId, chargeTypeId: chargeTypeId, errors: new Dictionary<string, string[]>
            {
                ["charge_type_id"] = ["Selected charge type was not found."]
            }));

    public static PayableChargeValidationException ChargeTypeDeleted(Guid chargeTypeId, Guid? documentId = null)
        => new(
            message: "Selected charge type is marked for deletion.",
            errorCode: "pm.validation.payable_charge.charge_type_deleted",
            context: BuildContext(documentId, chargeTypeId: chargeTypeId, errors: new Dictionary<string, string[]>
            {
                ["charge_type_id"] = ["Selected charge type is marked for deletion."]
            }));

    public static PayableChargeValidationException AmountMustBePositive(decimal amount, Guid? documentId = null)
        => new(
            message: "Amount must be positive.",
            errorCode: "pm.validation.payable_charge.amount_must_be_positive",
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
