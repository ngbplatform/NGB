using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Payables;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

public sealed class PayableApplyPostValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents)
    : IDocumentPostValidator
{
    public string TypeCode => PropertyManagementCodes.PayableApply;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(PayableApplyPostValidator));

        var apply = await readers.ReadPayableApplyHeadAsync(documentForUpdate.Id, ct);

        if (apply.Amount <= 0m)
            throw PayableApplyValidationException.AmountMustBePositive(apply.Amount);

        if (apply.CreditDocumentId == apply.ChargeDocumentId)
            throw PayableApplyValidationException.CreditSourceAndChargeMustDiffer(apply.CreditDocumentId, apply.ChargeDocumentId);

        var creditDocument = await documents.GetAsync(apply.CreditDocumentId, ct);
        if (creditDocument is null)
            throw PayableApplyValidationException.CreditSourceNotFound(apply.CreditDocumentId);

        if (!PayableCreditSourceResolver.IsCreditSourceDocumentType(creditDocument.TypeCode))
            throw PayableApplyValidationException.CreditSourceWrongType(apply.CreditDocumentId, creditDocument.TypeCode);

        if (creditDocument.Status != DocumentStatus.Posted)
            throw PayableApplyValidationException.CreditSourceMustBePosted(apply.CreditDocumentId, creditDocument.Status);

        var chargeDocument = await documents.GetAsync(apply.ChargeDocumentId, ct);
        if (chargeDocument is null)
            throw PayableApplyValidationException.ChargeNotFound(apply.ChargeDocumentId);

        if (!string.Equals(chargeDocument.TypeCode, PropertyManagementCodes.PayableCharge, StringComparison.OrdinalIgnoreCase))
            throw PayableApplyValidationException.ChargeWrongType(apply.ChargeDocumentId, chargeDocument.TypeCode);

        if (chargeDocument.Status != DocumentStatus.Posted)
            throw PayableApplyValidationException.ChargeMustBePosted(apply.ChargeDocumentId, chargeDocument.Status);

        var creditSource = await PayableCreditSourceResolver.ReadRequiredAsync(readers, creditDocument, ct);
        var charge = await readers.ReadPayableChargeHeadAsync(apply.ChargeDocumentId, ct);

        if (creditSource.PartyId != charge.PartyId || creditSource.PropertyId != charge.PropertyId)
            throw PayableApplyValidationException.PartyPropertyMismatch(apply.CreditDocumentId, apply.ChargeDocumentId);
    }
}
