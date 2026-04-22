using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class ReceivablePaymentValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static ReceivablePaymentValidationException LeaseNotFound(Guid leaseId, Guid? documentId = null)
        => new(
            message: "Selected lease was not found.",
            errorCode: "pm.validation.receivables.lease_not_found",
            context: BuildContext(
                documentId: documentId,
                leaseId: leaseId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["lease_id"] = ["Selected lease was not found."]
                }));

    public static ReceivablePaymentValidationException LeaseMarkedForDeletion(Guid leaseId, Guid? documentId = null)
        => new(
            message: "Selected lease is marked for deletion.",
            errorCode: "pm.validation.receivables.lease_deleted",
            context: BuildContext(
                documentId: documentId,
                leaseId: leaseId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["lease_id"] = ["Selected lease is marked for deletion."]
                }));

    public static ReceivablePaymentValidationException AmountMustBePositive(decimal amount, Guid? documentId = null)
        => new(
            message: "Amount must be positive.",
            errorCode: "pm.validation.receivable_payment.amount_must_be_positive",
            context: BuildContext(
                documentId: documentId,
                amount: amount,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["amount"] = ["Amount must be positive."]
                }));

    public static ReceivablePaymentValidationException PartyMismatch(
        Guid leaseId,
        Guid expectedPartyId,
        Guid actualPartyId,
        Guid? documentId = null)
        => new(
            message: "Selected tenant does not match the lease.",
            errorCode: "pm.validation.receivables.lease_party_mismatch",
            context: BuildContext(
                documentId: documentId,
                leaseId: leaseId,
                expectedPartyId: expectedPartyId,
                actualPartyId: actualPartyId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["party_id"] = ["Tenant must match the lease."],
                    ["lease_id"] = ["The selected lease belongs to a different tenant."]
                }));

    public static ReceivablePaymentValidationException PropertyMismatch(
        Guid leaseId,
        Guid expectedPropertyId,
        Guid actualPropertyId,
        Guid? documentId = null)
        => new(
            message: "Selected property does not match the lease.",
            errorCode: "pm.validation.receivables.lease_property_mismatch",
            context: BuildContext(
                documentId: documentId,
                leaseId: leaseId,
                expectedPropertyId: expectedPropertyId,
                actualPropertyId: actualPropertyId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["property_id"] = ["Property must match the lease."],
                    ["lease_id"] = ["The selected lease belongs to a different property."]
                }));

    public static ReceivablePaymentValidationException BankAccountNotFound(Guid bankAccountId, Guid? documentId = null)
        => new(
            message: "Selected bank account was not found.",
            errorCode: "pm.validation.receivable_payment.bank_account_not_found",
            context: BuildContext(
                documentId: documentId,
                bankAccountId: bankAccountId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["bank_account_id"] = ["Selected bank account was not found."]
                }));

    public static ReceivablePaymentValidationException BankAccountDeleted(Guid bankAccountId, Guid? documentId = null)
        => new(
            message: "Selected bank account is marked for deletion.",
            errorCode: "pm.validation.receivable_payment.bank_account_deleted",
            context: BuildContext(
                documentId: documentId,
                bankAccountId: bankAccountId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["bank_account_id"] = ["Selected bank account is marked for deletion."]
                }));

    private static IReadOnlyDictionary<string, object?> BuildContext(
        Guid? documentId = null,
        Guid? leaseId = null,
        Guid? expectedPartyId = null,
        Guid? actualPartyId = null,
        Guid? expectedPropertyId = null,
        Guid? actualPropertyId = null,
        Guid? bankAccountId = null,
        decimal? amount = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var ctx = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (documentId is not null)
            ctx["documentId"] = documentId.Value;
        if (leaseId is not null)
            ctx["leaseId"] = leaseId.Value;
        if (expectedPartyId is not null)
            ctx["expectedPartyId"] = expectedPartyId.Value;
        if (actualPartyId is not null)
            ctx["actualPartyId"] = actualPartyId.Value;
        if (expectedPropertyId is not null)
            ctx["expectedPropertyId"] = expectedPropertyId.Value;
        if (actualPropertyId is not null)
            ctx["actualPropertyId"] = actualPropertyId.Value;
        if (bankAccountId is not null)
            ctx["bankAccountId"] = bankAccountId.Value;
        if (amount is not null)
            ctx["amount"] = amount.Value;
        if (errors is not null)
            ctx["errors"] = errors;
        return ctx;
    }
}
