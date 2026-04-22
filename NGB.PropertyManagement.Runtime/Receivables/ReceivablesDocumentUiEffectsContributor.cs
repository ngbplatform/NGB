using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Effects;
using NGB.Contracts.Metadata;
using NGB.Core.Dimensions;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Receivables;

/// <summary>
/// PM contribution to document UI effects.
///
/// Current scope:
/// - enables/disables the "apply" action for apply-capable receivables documents
///   (charges and credit sources) based on current outstanding / available credit.
///
/// Implementation notes:
/// - Uses Operational Register (pm.receivables_open_items) movements filtered by dimensions
///   (party, property, lease, receivable_item) to compute net balance for a single item.
/// - Scan bounds are derived from lease start month and extended to the max posted month
///   for the specific item.
/// </summary>
public sealed class ReceivablesDocumentUiEffectsContributor(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents,
    IPropertyManagementAccountingPolicyReader policyReader,
    IOperationalRegisterMovementsQueryReader movements)
    : IDocumentUiEffectsContributor
{
    private const int PageSize = 5000;

    public async Task<IReadOnlyList<DocumentUiActionContributionDto>> ContributeAsync(
        string documentType,
        Guid documentId,
        RecordPayload payload,
        DocumentStatus status,
        CancellationToken ct)
    {
        if (!PropertyManagementCodes.IsApplyCapableDocumentType(documentType))
            return [];

        // UI rule: receivables can be applied only when the document is posted.
        if (status != DocumentStatus.Posted)
            return [ApplyDisabled("pm.ui.apply.requires_posted", "Apply is available only for posted receivables documents.")];

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
                return [new DocumentUiActionContributionDto("apply", IsAllowed: true, DisabledReasons: [])];

            return [ApplyDisabled("pm.ui.apply.no_outstanding", "Nothing to apply: outstanding amount is zero.")];
        }
        else
        {
            var creditSource = await ReceivableCreditSourceResolver.ReadRequiredAsync(readers, documents, documentId, ct);
            var net = await GetNetForItemAsync(creditSource.PartyId, creditSource.PropertyId, creditSource.LeaseId, itemId: documentId, ct);
            var credit = net < 0m ? -net : 0m;

            if (credit > 0m)
                return [new DocumentUiActionContributionDto("apply", IsAllowed: true, DisabledReasons: [])];

            return [ApplyDisabled("pm.ui.apply.no_credit", "Nothing to apply: available credit is zero.")];
        }
    }

    private static DocumentUiActionContributionDto ApplyDisabled(string code, string message)
        => new("apply", IsAllowed: false, DisabledReasons: [new DocumentUiActionReasonDto(code, message)]);

    private async Task<decimal> GetNetForItemAsync(
        Guid partyId,
        Guid propertyId,
        Guid leaseId,
        Guid itemId,
        CancellationToken ct)
    {
        var policy = await policyReader.GetRequiredAsync(ct);
        var lease = await readers.ReadLeaseHeadAsync(leaseId, ct);

        var leaseStartMonth = new DateOnly(lease.StartOnUtc.Year, lease.StartOnUtc.Month, 1);
        var nowMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var fromMonth = leaseStartMonth <= nowMonth ? leaseStartMonth : nowMonth;

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

        // Future-start leases can still have movements dated in the current month.
        // Use the current month as a safe baseline and extend upward when future-dated rows exist.
        var toMonth = await OperationalRegisterScanBoundaries.ResolveToMonthInclusiveAsync(
            movements,
            policy.ReceivablesOpenItemsOperationalRegisterId,
            fromMonth,
            nowMonth,
            dimensions: dims,
            ct: ct);

        var net = 0m;
        long? after = null;

        while (true)
        {
            var page = await movements.GetByMonthsAsync(
                policy.ReceivablesOpenItemsOperationalRegisterId,
                fromMonth,
                toMonth,
                dimensions: dims,
                afterMovementId: after,
                limit: PageSize,
                ct: ct);

            if (page.Count == 0)
                break;

            foreach (var row in page)
            {
                var amount = ReadSingleAmount(row.Values);
                if (amount == 0m)
                    continue;

                net += row.IsStorno ? -amount : amount;
            }

            after = page[^1].MovementId;
            if (page.Count < PageSize)
                break;
        }

        return net;
    }

    private static decimal ReadSingleAmount(IReadOnlyDictionary<string, decimal> values)
    {
        if (values.Count == 0)
            return 0m;

        // Open-items register is expected to have a single resource: "amount".
        if (values.TryGetValue("amount", out var v))
            return v;

        // Be tolerant in case resource column_code changes.
        return values.Values.FirstOrDefault();
    }
}
