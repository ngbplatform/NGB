using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using NGB.Core.Dimensions;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.DocumentActions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Receivables;

public interface IReceivablesApplyAvailabilitySource : IPropertyManagementApplyAvailabilitySource;

/// <summary>
/// Canonical availability source for receivables apply actions.
///
/// Current scope:
/// - enables/disables the "apply" action for apply-capable receivables documents
///   (charges and credit sources) based on current outstanding / available credit.
///
/// Implementation notes:
/// - Computes the net balance in PostgreSQL for the exact dimension filter. This keeps
///   availability checks O(1) in application memory and avoids loading/enriching movement pages.
/// </summary>
public sealed class ReceivablesApplyAvailabilitySource(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents,
    IPropertyManagementAccountingPolicyReader policyReader,
    IOperationalRegisterResourceNetReader resourceNetReader)
    : IReceivablesApplyAvailabilitySource
{
    public async Task<DocumentActionAvailabilityResult> EvaluateAsync(
        string documentType,
        Guid documentId,
        DocumentStatus status,
        CancellationToken ct)
    {
        if (!PropertyManagementCodes.IsApplyCapableDocumentType(documentType))
            return Disabled("pm.apply.unsupported_document_type", "This document type cannot be applied.");

        // UI rule: receivables can be applied only when the document is posted.
        if (status != DocumentStatus.Posted)
            return Disabled("pm.receivables.apply.requires_posted", "Apply is available only for posted receivables documents.");

        if (PropertyManagementCodes.IsChargeLikeDocumentType(documentType))
        {
            Guid partyId;
            Guid propertyId;
            Guid leaseId;

            if (string.Equals(documentType, PropertyManagementCodes.ReceivableCharge, StringComparison.OrdinalIgnoreCase))
            {
                var charge = await readers.ReadReceivableChargeHeadAsync(documentId, ct);
                partyId = charge.PartyId;
                propertyId = charge.PropertyId;
                leaseId = charge.LeaseId;
            }
            else if (string.Equals(documentType, PropertyManagementCodes.RentCharge, StringComparison.OrdinalIgnoreCase))
            {
                var charge = await readers.ReadRentChargeHeadAsync(documentId, ct);
                partyId = charge.PartyId;
                propertyId = charge.PropertyId;
                leaseId = charge.LeaseId;
            }
            else
            {
                var charge = await readers.ReadLateFeeChargeHeadAsync(documentId, ct);
                partyId = charge.PartyId;
                propertyId = charge.PropertyId;
                leaseId = charge.LeaseId;
            }

            var net = await GetNetForItemAsync(partyId, propertyId, leaseId, itemId: documentId, ct);
            var outstanding = net > 0m ? net : 0m;

            if (outstanding > 0m)
                return DocumentActionAvailabilityResult.Allowed;

            return Disabled("pm.receivables.apply.no_outstanding", "Nothing to apply: outstanding amount is zero.");
        }
        else
        {
            var creditSource = await ReceivableCreditSourceResolver.ReadRequiredAsync(readers, documents, documentId, ct);
            var net = await GetNetForItemAsync(creditSource.PartyId, creditSource.PropertyId, creditSource.LeaseId, itemId: documentId, ct);
            var credit = net < 0m ? -net : 0m;

            if (credit > 0m)
                return DocumentActionAvailabilityResult.Allowed;

            return Disabled("pm.receivables.apply.no_credit", "Nothing to apply: available credit is zero.");
        }
    }

    private static DocumentActionAvailabilityResult Disabled(string code, string message)
        => new([new DocumentActionDisabledReasonDto(code, message)]);

    private async Task<decimal> GetNetForItemAsync(
        Guid partyId,
        Guid propertyId,
        Guid leaseId,
        Guid itemId,
        CancellationToken ct)
    {
        var policy = await policyReader.GetRequiredAsync(ct);
        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");
        var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");

        var dims = new List<DimensionValue>(4)
        {
            new(partyDimId, partyId),
            new(propertyDimId, propertyId),
            new(leaseDimId, leaseId),
            new(itemDimId, itemId)
        };

        return await resourceNetReader.GetNetByDimensionsAsync(
            policy.ReceivablesOpenItemsOperationalRegisterId,
            dims,
            resourceColumnCode: "amount",
            ct);
    }
}
