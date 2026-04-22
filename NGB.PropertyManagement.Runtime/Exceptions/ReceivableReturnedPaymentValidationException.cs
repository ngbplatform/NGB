using NGB.Core.Documents;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class ReceivableReturnedPaymentValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static ReceivableReturnedPaymentValidationException LeaseNotFound(Guid leaseId, Guid? documentId = null)
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

    public static ReceivableReturnedPaymentValidationException LeaseMarkedForDeletion(
        Guid leaseId,
        Guid? documentId = null)
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

    public static ReceivableReturnedPaymentValidationException AmountMustBePositive(
        decimal amount,
        Guid? documentId = null)
        => new(
            message: "Amount must be positive.",
            errorCode: "pm.validation.receivable_returned_payment.amount_must_be_positive",
            context: BuildContext(
                documentId: documentId,
                amount: amount,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["amount"] = ["Amount must be positive."]
                }));

    public static ReceivableReturnedPaymentValidationException PartyMismatch(
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

    public static ReceivableReturnedPaymentValidationException PropertyMismatch(
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

    public static ReceivableReturnedPaymentValidationException OriginalPaymentNotFound(
        Guid originalPaymentId,
        Guid? documentId = null)
        => new(
            message: "Selected original payment was not found.",
            errorCode: "pm.validation.receivable_returned_payment.original_payment_not_found",
            context: BuildContext(
                documentId: documentId,
                originalPaymentId: originalPaymentId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["original_payment_id"] = ["Selected original payment was not found."]
                }));

    public static ReceivableReturnedPaymentValidationException OriginalPaymentMustBePosted(
        Guid originalPaymentId,
        DocumentStatus status,
        Guid? documentId = null)
        => new(
            message: "Selected original payment must be posted.",
            errorCode: "pm.validation.receivable_returned_payment.original_payment_must_be_posted",
            context: BuildContext(
                documentId: documentId,
                originalPaymentId: originalPaymentId,
                status: status.ToString(),
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["original_payment_id"] = ["Selected original payment must be posted."]
                }));

    public static ReceivableReturnedPaymentValidationException OriginalPaymentMismatch(
        Guid originalPaymentId,
        Guid expectedPartyId,
        Guid actualPartyId,
        Guid expectedPropertyId,
        Guid actualPropertyId,
        Guid expectedLeaseId,
        Guid actualLeaseId,
        Guid? documentId = null)
        => new(
            message: "Original payment must match tenant, property, and lease.",
            errorCode: "pm.validation.receivable_returned_payment.original_payment_context_mismatch",
            context: BuildContext(
                documentId: documentId,
                originalPaymentId: originalPaymentId,
                expectedPartyId: expectedPartyId,
                actualPartyId: actualPartyId,
                expectedPropertyId: expectedPropertyId,
                actualPropertyId: actualPropertyId,
                expectedLeaseId: expectedLeaseId,
                actualLeaseId: actualLeaseId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["original_payment_id"] = ["Original payment must match tenant, property, and lease."],
                    ["party_id"] = ["Tenant must match the original payment."],
                    ["property_id"] = ["Property must match the original payment."],
                    ["lease_id"] = ["Lease must match the original payment."]
                }));

    public static ReceivableReturnedPaymentValidationException ReturnedOnBeforeOriginalPayment(
        Guid originalPaymentId,
        DateOnly originalReceivedOnUtc,
        DateOnly returnedOnUtc,
        Guid? documentId = null)
        => new(
            message: "Returned On cannot be earlier than the original payment date.",
            errorCode: "pm.validation.receivable_returned_payment.returned_on_before_original_payment",
            context: BuildContext(
                documentId: documentId,
                originalPaymentId: originalPaymentId,
                originalReceivedOnUtc: originalReceivedOnUtc,
                returnedOnUtc: returnedOnUtc,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["returned_on_utc"] = ["Returned On cannot be earlier than the original payment date."],
                    ["original_payment_id"] = ["Returned On cannot be earlier than the original payment date."]
                }));

    public static ReceivableReturnedPaymentValidationException InsufficientAvailableCredit(
        Guid originalPaymentId,
        decimal requestedAmount,
        decimal availableCredit,
        Guid? documentId = null)
        => new(
            message: "Returned amount exceeds the remaining unapplied credit of the original payment.",
            errorCode: "pm.validation.receivable_returned_payment.insufficient_available_credit",
            context: BuildContext(
                documentId: documentId,
                originalPaymentId: originalPaymentId,
                requestedAmount: requestedAmount,
                availableCredit: availableCredit,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["amount"] = ["Returned amount exceeds the remaining unapplied credit of the original payment."],
                    ["original_payment_id"] = ["Unapply allocations first or reduce the returned amount."]
                }));

    public static ReceivableReturnedPaymentValidationException BankAccountNotFound(
        Guid bankAccountId,
        Guid? documentId = null)
        => new(
            message: "Selected bank account was not found.",
            errorCode: "pm.validation.receivable_returned_payment.bank_account_not_found",
            context: BuildContext(
                documentId: documentId,
                bankAccountId: bankAccountId,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["bank_account_id"] = ["Selected bank account was not found."]
                }));

    public static ReceivableReturnedPaymentValidationException BankAccountDeleted(
        Guid bankAccountId,
        Guid? documentId = null)
        => new(
            message: "Selected bank account is marked for deletion.",
            errorCode: "pm.validation.receivable_returned_payment.bank_account_deleted",
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
        Guid? originalPaymentId = null,
        Guid? expectedPartyId = null,
        Guid? actualPartyId = null,
        Guid? expectedPropertyId = null,
        Guid? actualPropertyId = null,
        Guid? expectedLeaseId = null,
        Guid? actualLeaseId = null,
        Guid? bankAccountId = null,
        decimal? amount = null,
        decimal? requestedAmount = null,
        decimal? availableCredit = null,
        DateOnly? originalReceivedOnUtc = null,
        DateOnly? returnedOnUtc = null,
        string? status = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var ctx = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (documentId is not null)
            ctx["documentId"] = documentId.Value;
        if (leaseId is not null)
            ctx["leaseId"] = leaseId.Value;
        if (originalPaymentId is not null)
            ctx["originalPaymentId"] = originalPaymentId.Value;
        if (expectedPartyId is not null)
            ctx["expectedPartyId"] = expectedPartyId.Value;
        if (actualPartyId is not null)
            ctx["actualPartyId"] = actualPartyId.Value;
        if (expectedPropertyId is not null)
            ctx["expectedPropertyId"] = expectedPropertyId.Value;
        if (actualPropertyId is not null)
            ctx["actualPropertyId"] = actualPropertyId.Value;
        if (expectedLeaseId is not null)
            ctx["expectedLeaseId"] = expectedLeaseId.Value;
        if (actualLeaseId is not null)
            ctx["actualLeaseId"] = actualLeaseId.Value;
        if (bankAccountId is not null)
            ctx["bankAccountId"] = bankAccountId.Value;
        if (amount is not null)
            ctx["amount"] = amount.Value;
        if (requestedAmount is not null)
            ctx["requestedAmount"] = requestedAmount.Value;
        if (availableCredit is not null)
            ctx["availableCredit"] = availableCredit.Value;
        if (originalReceivedOnUtc is not null)
            ctx["originalReceivedOnUtc"] = originalReceivedOnUtc.Value;
        if (returnedOnUtc is not null)
            ctx["returnedOnUtc"] = returnedOnUtc.Value;
        if (!string.IsNullOrWhiteSpace(status))
            ctx["status"] = status;
        if (errors is not null)
            ctx["errors"] = errors;
        return ctx;
    }
}
