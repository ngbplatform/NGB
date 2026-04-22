using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class PayablePaymentValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static PayablePaymentValidationException VendorNotFound(Guid partyId, Guid? documentId = null)
        => new(
            message: "Selected vendor was not found.",
            errorCode: "pm.validation.payable_payment.vendor_not_found",
            context: BuildContext(documentId, partyId: partyId, errors: new Dictionary<string, string[]>
            {
                ["party_id"] = ["Selected vendor was not found."]
            }));

    public static PayablePaymentValidationException VendorDeleted(Guid partyId, Guid? documentId = null)
        => new(
            message: "Selected vendor is marked for deletion.",
            errorCode: "pm.validation.payable_payment.vendor_deleted",
            context: BuildContext(documentId, partyId: partyId, errors: new Dictionary<string, string[]>
            {
                ["party_id"] = ["Selected vendor is marked for deletion."]
            }));

    public static PayablePaymentValidationException VendorRoleRequired(Guid partyId, Guid? documentId = null)
        => new(
            message: "Selected party must have Vendor role enabled.",
            errorCode: "pm.validation.payable_payment.vendor_required",
            context: BuildContext(documentId, partyId: partyId, errors: new Dictionary<string, string[]>
            {
                ["party_id"] = ["Selected party must have Vendor role enabled."]
            }));

    public static PayablePaymentValidationException PropertyNotFound(Guid propertyId, Guid? documentId = null)
        => new(
            message: "Selected property was not found.",
            errorCode: "pm.validation.payable_payment.property_not_found",
            context: BuildContext(documentId, propertyId: propertyId, errors: new Dictionary<string, string[]>
            {
                ["property_id"] = ["Selected property was not found."]
            }));

    public static PayablePaymentValidationException PropertyDeleted(Guid propertyId, Guid? documentId = null)
        => new(
            message: "Selected property is marked for deletion.",
            errorCode: "pm.validation.payable_payment.property_deleted",
            context: BuildContext(documentId, propertyId: propertyId, errors: new Dictionary<string, string[]>
            {
                ["property_id"] = ["Selected property is marked for deletion."]
            }));

    public static PayablePaymentValidationException AmountMustBePositive(decimal amount, Guid? documentId = null)
        => new(
            message: "Amount must be positive.",
            errorCode: "pm.validation.payable_payment.amount_must_be_positive",
            context: BuildContext(documentId, amount: amount, errors: new Dictionary<string, string[]>
            {
                ["amount"] = ["Amount must be positive."]
            }));

    public static PayablePaymentValidationException BankAccountNotFound(Guid bankAccountId, Guid? documentId = null)
        => new(
            message: "Selected bank account was not found.",
            errorCode: "pm.validation.payable_payment.bank_account_not_found",
            context: BuildContext(documentId, bankAccountId: bankAccountId, errors: new Dictionary<string, string[]>
            {
                ["bank_account_id"] = ["Selected bank account was not found."]
            }));

    public static PayablePaymentValidationException BankAccountDeleted(Guid bankAccountId, Guid? documentId = null)
        => new(
            message: "Selected bank account is marked for deletion.",
            errorCode: "pm.validation.payable_payment.bank_account_deleted",
            context: BuildContext(documentId, bankAccountId: bankAccountId, errors: new Dictionary<string, string[]>
            {
                ["bank_account_id"] = ["Selected bank account is marked for deletion."]
            }));

    private static IReadOnlyDictionary<string, object?> BuildContext(
        Guid? documentId = null,
        Guid? partyId = null,
        Guid? propertyId = null,
        Guid? bankAccountId = null,
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
        if (bankAccountId is not null)
            ctx["bankAccountId"] = bankAccountId.Value;
        if (amount is not null)
            ctx["amount"] = amount.Value;
        if (errors is not null)
            ctx["errors"] = errors;
        return ctx;
    }
}
