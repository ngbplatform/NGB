using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Operational Registers posting for pm.receivable_returned_payment into the receivables open-items register.
///
/// Semantics:
/// - Writes +amount to the ORIGINAL payment item (pm.receivable_item = original_payment_id).
/// - This reduces/removes the remaining unapplied credit of the original receipt instead of creating a new open item.
/// - No charge item is created; users must unapply first if the original payment no longer has enough free credit.
/// - The link to <c>original_payment_id</c> intentionally remains an explicit persisted <c>based_on</c>
///   relationship created by runtime/posting logic rather than mirrored-field metadata.
/// </summary>
public sealed class ReceivableReturnedPaymentOpenItemsOperationalRegisterPostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceNetReader netReader,
    IDimensionSetService dimensionSets,
    IDocumentRelationshipService relationships,
    IAdvisoryLockManager locks)
    : IDocumentOperationalRegisterPostingHandler
{
    private const string BasedOnRelationshipCode = "based_on";

    public string TypeCode => PropertyManagementCodes.ReceivableReturnedPayment;

    public async Task BuildMovementsAsync(
        DocumentRecord document,
        IOperationalRegisterMovementsBuilder builder,
        CancellationToken ct)
    {
        var returned = await readers.ReadReceivableReturnedPaymentHeadAsync(document.Id, ct);
        if (returned.Amount <= 0m)
            throw ReceivableReturnedPaymentValidationException.AmountMustBePositive(returned.Amount, document.Id);

        if (returned.OriginalPaymentId == Guid.Empty)
            throw new NgbArgumentRequiredException("original_payment_id");

        await locks.LockDocumentAsync(returned.OriginalPaymentId, ct);

        if (document.Status == DocumentStatus.Draft)
        {
            await relationships.CreateAsync(
                fromDocumentId: document.Id,
                toDocumentId: returned.OriginalPaymentId,
                relationshipCode: BasedOnRelationshipCode,
                manageTransaction: false,
                ct: ct);
        }

        var originalPayment = await readers.ReadReceivablePaymentHeadAsync(returned.OriginalPaymentId, ct);
        if (originalPayment.PartyId != returned.PartyId
            || originalPayment.PropertyId != returned.PropertyId
            || originalPayment.LeaseId != returned.LeaseId)
        {
            throw ReceivableReturnedPaymentValidationException.OriginalPaymentMismatch(
                originalPaymentId: returned.OriginalPaymentId,
                expectedPartyId: returned.PartyId,
                actualPartyId: originalPayment.PartyId,
                expectedPropertyId: returned.PropertyId,
                actualPropertyId: originalPayment.PropertyId,
                expectedLeaseId: returned.LeaseId,
                actualLeaseId: originalPayment.LeaseId,
                documentId: document.Id);
        }

        if (returned.ReturnedOnUtc < originalPayment.ReceivedOnUtc)
        {
            throw ReceivableReturnedPaymentValidationException.ReturnedOnBeforeOriginalPayment(
                originalPaymentId: returned.OriginalPaymentId,
                originalReceivedOnUtc: originalPayment.ReceivedOnUtc,
                returnedOnUtc: returned.ReturnedOnUtc,
                documentId: document.Id);
        }

        var policy = await policyReader.GetRequiredAsync(ct);
        var reg = await registers.GetByIdAsync(policy.ReceivablesOpenItemsOperationalRegisterId, ct);
        if (reg is null)
            throw new NgbConfigurationViolationException(
                $"Operational register '{policy.ReceivablesOpenItemsOperationalRegisterId}' referenced by '{PropertyManagementCodes.AccountingPolicy}' was not found.");

        var occurredAtUtc = new DateTime(returned.ReturnedOnUtc.Year, returned.ReturnedOnUtc.Month, returned.ReturnedOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");
        var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");

        var paymentBag = new DimensionBag([
            new DimensionValue(partyDimId, originalPayment.PartyId),
            new DimensionValue(propertyDimId, originalPayment.PropertyId),
            new DimensionValue(leaseDimId, originalPayment.LeaseId),
            new DimensionValue(itemDimId, returned.OriginalPaymentId)
        ]);

        var paymentDimSetId = await dimensionSets.GetOrCreateIdAsync(paymentBag, ct);
        var paymentNet = await netReader.GetNetByDimensionSetAsync(reg.RegisterId, paymentDimSetId, resourceColumnCode: "amount", ct);
        if (document.Status == DocumentStatus.Posted)
            paymentNet -= returned.Amount;

        var availableCredit = paymentNet >= 0m ? 0m : -paymentNet;
        if (availableCredit < returned.Amount)
        {
            throw ReceivableReturnedPaymentValidationException.InsufficientAvailableCredit(
                originalPaymentId: returned.OriginalPaymentId,
                requestedAmount: returned.Amount,
                availableCredit: availableCredit,
                documentId: document.Id);
        }

        builder.Add(
            registerCode: reg.Code,
            new OperationalRegisterMovement(
                DocumentId: document.Id,
                OccurredAtUtc: occurredAtUtc,
                DimensionSetId: paymentDimSetId,
                Resources: new Dictionary<string, decimal> { ["amount"] = returned.Amount }));
    }
}
