using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Receivables;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

/// <summary>
/// Posting-time safety net for pm.receivable_apply.
/// </summary>
public sealed class ReceivableApplyPostValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents)
    : IDocumentPostValidator
{
    public string TypeCode => PropertyManagementCodes.ReceivableApply;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(ReceivableApplyPostValidator));

        var apply = await readers.ReadReceivableApplyHeadAsync(documentForUpdate.Id, ct);

        if (apply.Amount <= 0m)
            throw ReceivableApplyValidationException.AmountMustBePositive(apply.Amount);

        if (apply.CreditDocumentId == apply.ChargeDocumentId)
            throw ReceivableApplyValidationException.PaymentAndChargeMustMatch(apply.CreditDocumentId, apply.ChargeDocumentId);

        var paymentDocument = await documents.GetAsync(apply.CreditDocumentId, ct);
        if (paymentDocument is null)
            throw ReceivableApplyValidationException.PaymentNotFound(apply.CreditDocumentId);

        if (!ReceivableCreditSourceResolver.IsCreditSourceDocumentType(paymentDocument.TypeCode))
            throw ReceivableApplyValidationException.PaymentWrongType(apply.CreditDocumentId, paymentDocument.TypeCode);

        if (paymentDocument.Status != DocumentStatus.Posted)
            throw ReceivableApplyValidationException.PaymentMustBePosted(apply.CreditDocumentId, paymentDocument.Status);

        var chargeDocument = await documents.GetAsync(apply.ChargeDocumentId, ct);
        if (chargeDocument is null)
            throw ReceivableApplyValidationException.ChargeNotFound(apply.ChargeDocumentId);

        if (!PropertyManagementCodes.IsChargeLikeDocumentType(chargeDocument.TypeCode))
            throw ReceivableApplyValidationException.ChargeWrongType(apply.ChargeDocumentId, chargeDocument.TypeCode);

        if (chargeDocument.Status != DocumentStatus.Posted)
            throw ReceivableApplyValidationException.ChargeMustBePosted(apply.ChargeDocumentId, chargeDocument.Status);

        var creditSource = await ReceivableCreditSourceResolver.ReadRequiredAsync(readers, paymentDocument, ct);
        var charge = await ReadChargeLikeContextAsync(apply.ChargeDocumentId, chargeDocument.TypeCode, ct);

        if (creditSource.PartyId != charge.PartyId || creditSource.PropertyId != charge.PropertyId || creditSource.LeaseId != charge.LeaseId)
            throw ReceivableApplyValidationException.PartyPropertyLeaseMismatch(apply.CreditDocumentId, apply.ChargeDocumentId);
    }

    private async Task<ChargeLikeContext> ReadChargeLikeContextAsync(
        Guid chargeDocumentId,
        string chargeTypeCode,
        CancellationToken ct)
    {
        if (string.Equals(chargeTypeCode, PropertyManagementCodes.ReceivableCharge, StringComparison.OrdinalIgnoreCase))
        {
            var charge = await readers.ReadReceivableChargeHeadAsync(chargeDocumentId, ct);
            return new ChargeLikeContext(charge.PartyId, charge.PropertyId, charge.LeaseId);
        }

        if (string.Equals(chargeTypeCode, PropertyManagementCodes.LateFeeCharge, StringComparison.OrdinalIgnoreCase))
        {
            var charge = await readers.ReadLateFeeChargeHeadAsync(chargeDocumentId, ct);
            return new ChargeLikeContext(charge.PartyId, charge.PropertyId, charge.LeaseId);
        }

        if (string.Equals(chargeTypeCode, PropertyManagementCodes.RentCharge, StringComparison.OrdinalIgnoreCase))
        {
            var charge = await readers.ReadRentChargeHeadAsync(chargeDocumentId, ct);
            return new ChargeLikeContext(charge.PartyId, charge.PropertyId, charge.LeaseId);
        }

        throw ReceivableApplyValidationException.ChargeWrongType(chargeDocumentId, chargeTypeCode);
    }

    private readonly record struct ChargeLikeContext(Guid PartyId, Guid PropertyId, Guid LeaseId);
}
