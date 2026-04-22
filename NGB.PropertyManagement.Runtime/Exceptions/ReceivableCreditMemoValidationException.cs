using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class ReceivableCreditMemoValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static ReceivableCreditMemoValidationException LeaseNotFound(Guid leaseId, Guid? documentId = null)
        => new(
            message: "Selected lease was not found.",
            errorCode: "pm.validation.receivable_credit_memo.lease_not_found",
            context: BuildContext(documentId, leaseId: leaseId, errors: new Dictionary<string, string[]>
            {
                ["lease_id"] = ["Selected lease was not found."]
            }));

    public static ReceivableCreditMemoValidationException LeaseMarkedForDeletion(Guid leaseId, Guid? documentId = null)
        => new(
            message: "Selected lease is marked for deletion.",
            errorCode: "pm.validation.receivable_credit_memo.lease_deleted",
            context: BuildContext(documentId, leaseId: leaseId, errors: new Dictionary<string, string[]>
            {
                ["lease_id"] = ["Selected lease is marked for deletion."]
            }));

    public static ReceivableCreditMemoValidationException ClassificationRequired(Guid? documentId = null)
        => new(
            message: "Select a charge type.",
            errorCode: "pm.validation.receivable_credit_memo.classification_required",
            context: BuildContext(documentId, errors: new Dictionary<string, string[]>
            {
                ["charge_type_id"] = ["Charge Type is required."]
            }));

    public static ReceivableCreditMemoValidationException ChargeTypeNotFound(Guid chargeTypeId, Guid? documentId = null)
        => new(
            message: "Selected charge type was not found.",
            errorCode: "pm.validation.receivable_credit_memo.charge_type_not_found",
            context: BuildContext(documentId, chargeTypeId: chargeTypeId, errors: new Dictionary<string, string[]>
            {
                ["charge_type_id"] = ["Selected charge type was not found."]
            }));

    public static ReceivableCreditMemoValidationException AmountMustBePositive(decimal amount, Guid? documentId = null)
        => new(
            message: "Amount must be positive.",
            errorCode: "pm.validation.receivable_credit_memo.amount_must_be_positive",
            context: BuildContext(documentId, amount: amount, errors: new Dictionary<string, string[]>
            {
                ["amount"] = ["Amount must be positive."]
            }));

    private static IReadOnlyDictionary<string, object?> BuildContext(
        Guid? documentId = null,
        Guid? leaseId = null,
        Guid? chargeTypeId = null,
        decimal? amount = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var ctx = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (documentId is not null)
            ctx["documentId"] = documentId.Value;
        if (leaseId is not null)
            ctx["leaseId"] = leaseId.Value;
        if (chargeTypeId is not null)
            ctx["chargeTypeId"] = chargeTypeId.Value;
        if (amount is not null)
            ctx["amount"] = amount.Value;
        if (errors is not null)
            ctx["errors"] = errors;
        return ctx;
    }
}
