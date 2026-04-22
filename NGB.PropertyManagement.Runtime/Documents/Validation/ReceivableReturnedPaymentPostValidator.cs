using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Dimensions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

public sealed class ReceivableReturnedPaymentPostValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents,
    IPropertyManagementAccountingPolicyReader policyReader,
    IPropertyManagementPartyReader parties,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceNetReader netReader,
    IDimensionSetService dimensionSets)
    : IDocumentPostValidator
{
    public string TypeCode => PropertyManagementCodes.ReceivableReturnedPayment;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(ReceivableReturnedPaymentPostValidator));

        var returned = await readers.ReadReceivableReturnedPaymentHeadAsync(documentForUpdate.Id, ct);
        var leaseDocument = await documents.GetAsync(returned.LeaseId, ct);
        if (leaseDocument is null
            || !string.Equals(leaseDocument.TypeCode, PropertyManagementCodes.Lease, StringComparison.OrdinalIgnoreCase))
        {
            throw ReceivableReturnedPaymentValidationException.LeaseNotFound(returned.LeaseId, documentForUpdate.Id);
        }

        if (leaseDocument.Status == DocumentStatus.MarkedForDeletion)
            throw ReceivableReturnedPaymentValidationException.LeaseMarkedForDeletion(returned.LeaseId, documentForUpdate.Id);

        if (returned.Amount <= 0m)
            throw ReceivableReturnedPaymentValidationException.AmountMustBePositive(returned.Amount, documentForUpdate.Id);

        var property = await readers.ReadPropertyHeadAsync(returned.PropertyId, ct);
        if (property is null)
            throw new DocumentPropertyNotFoundException(TypeCode, returned.PropertyId);

        if (property.IsDeleted)
            throw new DocumentPropertyDeletedException(TypeCode, returned.PropertyId);

        if (!string.Equals(property.Kind, "Unit", StringComparison.OrdinalIgnoreCase))
            throw new DocumentPropertyMustBeUnitException(TypeCode, returned.PropertyId, property.Kind);

        var originalPaymentDocument = await documents.GetAsync(returned.OriginalPaymentId, ct);
        if (originalPaymentDocument is null
            || !string.Equals(originalPaymentDocument.TypeCode, PropertyManagementCodes.ReceivablePayment,
                StringComparison.OrdinalIgnoreCase))
        {
            throw ReceivableReturnedPaymentValidationException.OriginalPaymentNotFound(returned.OriginalPaymentId, documentForUpdate.Id);
        }

        if (originalPaymentDocument.Status != DocumentStatus.Posted)
            throw ReceivableReturnedPaymentValidationException.OriginalPaymentMustBePosted(returned.OriginalPaymentId, originalPaymentDocument.Status, documentForUpdate.Id);

        var originalPayment = await readers.ReadReceivablePaymentHeadAsync(returned.OriginalPaymentId, ct);
        await PartyRoleValidationGuards.EnsureTenantPartyAsync(TypeCode, "original_payment_id", originalPayment.PartyId, parties, ct);

        if (returned.ReturnedOnUtc < originalPayment.ReceivedOnUtc)
        {
            throw ReceivableReturnedPaymentValidationException.ReturnedOnBeforeOriginalPayment(
                originalPaymentId: returned.OriginalPaymentId,
                originalReceivedOnUtc: originalPayment.ReceivedOnUtc,
                returnedOnUtc: returned.ReturnedOnUtc,
                documentId: documentForUpdate.Id);
        }

        var policy = await policyReader.GetRequiredAsync(ct);
        var reg = await registers.GetByIdAsync(policy.ReceivablesOpenItemsOperationalRegisterId, ct);
        if (reg is null)
            return;

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");
        var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");

        var bag = new DimensionBag([
            new DimensionValue(partyDimId, originalPayment.PartyId),
            new DimensionValue(propertyDimId, originalPayment.PropertyId),
            new DimensionValue(leaseDimId, originalPayment.LeaseId),
            new DimensionValue(itemDimId, returned.OriginalPaymentId)
        ]);

        var dimensionSetId = await dimensionSets.GetOrCreateIdAsync(bag, ct);
        var paymentNet = await netReader.GetNetByDimensionSetAsync(reg.RegisterId, dimensionSetId, resourceColumnCode: "amount", ct);
        if (documentForUpdate.Status == DocumentStatus.Posted)
            paymentNet -= returned.Amount;

        var availableCredit = paymentNet >= 0m ? 0m : -paymentNet;
        if (availableCredit < returned.Amount)
        {
            throw ReceivableReturnedPaymentValidationException.InsufficientAvailableCredit(
                originalPaymentId: returned.OriginalPaymentId,
                requestedAmount: returned.Amount,
                availableCredit: availableCredit,
                documentId: documentForUpdate.Id);
        }
    }
}
