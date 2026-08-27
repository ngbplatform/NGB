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

public interface IReceivablesApplyAvailabilitySource : IPropertyManagementApplyAvailabilitySource
{
    Task<IReadOnlySet<Guid>> GetExhaustedPaymentIdsAsync(IReadOnlyCollection<Guid> paymentIds, CancellationToken ct);
}

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
    public async Task<IReadOnlySet<Guid>> GetExhaustedPaymentIdsAsync(
        IReadOnlyCollection<Guid> paymentIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paymentIds);

        var ids = paymentIds.Where(static id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
            return new HashSet<Guid>();

        var documentsById = await documents.GetByIdsAsync(ids, ct);
        var postedIds = new List<Guid>(ids.Length);
        var exhausted = new HashSet<Guid>();

        foreach (var id in ids)
        {
            if (!documentsById.TryGetValue(id, out var document))
                throw new NGB.Core.Documents.Exceptions.DocumentNotFoundException(id);

            if (!string.Equals(document.TypeCode, PropertyManagementCodes.ReceivablePayment, StringComparison.OrdinalIgnoreCase))
            {
                throw new NGB.Core.Documents.Exceptions.DocumentTypeMismatchException(
                    id,
                    PropertyManagementCodes.ReceivablePayment,
                    document.TypeCode);
            }

            if (document.Status == NGB.Core.Documents.DocumentStatus.Posted)
                postedIds.Add(id);
            else
                exhausted.Add(id);
        }

        if (postedIds.Count == 0)
            return exhausted;

        var heads = (await readers.ReadReceivablePaymentHeadsAsync(postedIds, ct))
            .ToDictionary(static head => head.DocumentId);
        var policy = await policyReader.GetRequiredAsync(ct);
        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");
        var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");
        var groups = new List<IReadOnlyList<DimensionValue>>(postedIds.Count);

        foreach (var paymentId in postedIds)
        {
            if (!heads.TryGetValue(paymentId, out var head))
                throw new NGB.Tools.Exceptions.NgbConfigurationViolationException($"Receivable payment '{paymentId}' has no typed head row.");

            groups.Add(
            [
                new DimensionValue(partyDimId, head.PartyId),
                new DimensionValue(propertyDimId, head.PropertyId),
                new DimensionValue(leaseDimId, head.LeaseId),
                new DimensionValue(itemDimId, paymentId)
            ]);
        }

        var nets = await resourceNetReader.GetNetsByDimensionsAsync(
            policy.ReceivablesOpenItemsOperationalRegisterId,
            groups,
            resourceColumnCode: "amount",
            asOfInclusive: DateOnly.MaxValue,
            ct);

        for (var index = 0; index < postedIds.Count; index++)
        {
            if (nets[index] >= 0m)
                exhausted.Add(postedIds[index]);
        }

        return exhausted;
    }

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
