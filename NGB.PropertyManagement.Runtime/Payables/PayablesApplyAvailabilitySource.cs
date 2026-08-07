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
    IOperationalRegisterMovementsQueryReader movements)
    : IPropertyManagementApplyAvailabilitySource
{
    private const int PageSize = 5000;

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
        var fromMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var activityStart = await readers.ReadFirstPayablesActivityMonthAsync(partyId, propertyId, ct);
        
        if (activityStart is not null && activityStart.Value < fromMonth)
            fromMonth = activityStart.Value;

        var toMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.PayableItem}");

        var dims = new List<DimensionValue>
        {
            new(partyDimId, partyId),
            new(propertyDimId, propertyId),
            new(itemDimId, itemId)
        };

        var maxMonth = await movements.GetMaxPeriodMonthAsync(policy.PayablesOpenItemsOperationalRegisterId, dimensions: dims, ct: ct);
        if (maxMonth is not null)
        {
            var m = new DateOnly(maxMonth.Value.Year, maxMonth.Value.Month, 1);
            if (m > toMonth)
                toMonth = m;
        }

        var net = 0m;
        long? after = null;
        
        while (true)
        {
            var page = await movements.GetByMonthsAsync(
                policy.PayablesOpenItemsOperationalRegisterId,
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
        => values.TryGetValue("amount", out var v) ? v : values.Values.FirstOrDefault();
}
