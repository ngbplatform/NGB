using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using NGB.Core.Dimensions;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.DocumentActions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Payables;

public sealed class PayablesApplyAvailabilitySource(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader,
    IOperationalRegisterResourceNetReader resourceNetReader)
    : IPropertyManagementApplyAvailabilitySource
{
    public async Task<DocumentActionAvailabilityResult> EvaluateAsync(
        string documentType,
        Guid documentId,
        DocumentStatus status,
        CancellationToken ct)
    {
        if (!PropertyManagementCodes.IsPayablesApplyCapableDocumentType(documentType))
            return Disabled("pm.payables.apply.unsupported_document_type", "This document type cannot be applied.");

        if (status != DocumentStatus.Posted)
            return Disabled("pm.payables.apply.requires_posted", "Apply is available only for posted payables documents.");

        if (string.Equals(documentType, PropertyManagementCodes.PayableCharge, StringComparison.OrdinalIgnoreCase))
        {
            var charge = await readers.ReadPayableChargeHeadAsync(documentId, ct);
            var net = await GetNetForItemAsync(charge.PartyId, charge.PropertyId, documentId, ct);
            var outstanding = net > 0m ? net : 0m;

            return outstanding > 0m
                ? DocumentActionAvailabilityResult.Allowed
                : Disabled("pm.payables.apply.no_outstanding", "Nothing to apply: outstanding amount is zero.");
        }

        Guid partyId;
        Guid propertyId;
        if (string.Equals(documentType, PropertyManagementCodes.PayablePayment, StringComparison.OrdinalIgnoreCase))
        {
            var payment = await readers.ReadPayablePaymentHeadAsync(documentId, ct);
            partyId = payment.PartyId;
            propertyId = payment.PropertyId;
        }
        else
        {
            var creditMemo = await readers.ReadPayableCreditMemoHeadAsync(documentId, ct);
            partyId = creditMemo.PartyId;
            propertyId = creditMemo.PropertyId;
        }

        var creditNet = await GetNetForItemAsync(partyId, propertyId, documentId, ct);
        var credit = creditNet < 0m ? -creditNet : 0m;

        return credit > 0m
            ? DocumentActionAvailabilityResult.Allowed
            : Disabled("pm.payables.apply.no_credit", "Nothing to apply: available credit is zero.");
    }

    private static DocumentActionAvailabilityResult Disabled(string code, string message)
        => new([new DocumentActionDisabledReasonDto(code, message)]);

    private async Task<decimal> GetNetForItemAsync(Guid partyId, Guid propertyId, Guid itemId, CancellationToken ct)
    {
        var policy = await policyReader.GetRequiredAsync(ct);
        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.PayableItem}");

        var dims = new List<DimensionValue>
        {
            new(partyDimId, partyId),
            new(propertyDimId, propertyId),
            new(itemDimId, itemId)
        };

        return await resourceNetReader.GetNetByDimensionsAsync(
            policy.PayablesOpenItemsOperationalRegisterId,
            dims,
            resourceColumnCode: "amount",
            ct);
    }
}
