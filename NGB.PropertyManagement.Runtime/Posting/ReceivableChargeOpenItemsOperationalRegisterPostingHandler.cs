using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Dimensions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Operational Registers posting for pm.receivable_charge into the open-items register.
///
/// Semantics:
/// - Writes +amount into policy.ReceivablesOpenItemsOperationalRegisterId
/// - OccurredAtUtc = due_on_utc (00:00:00Z)
/// - Dimensions: (pm.party, pm.property, pm.lease, pm.receivable_item)
///   where pm.receivable_item valueId = charge document id.
/// </summary>
public sealed class ReceivableChargeOpenItemsOperationalRegisterPostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader,
    IOperationalRegisterRepository registers,
    IDimensionSetService dimensionSets)
    : IDocumentOperationalRegisterPostingHandler
{
    public string TypeCode => PropertyManagementCodes.ReceivableCharge;

    public async Task BuildMovementsAsync(
        DocumentRecord document,
        IOperationalRegisterMovementsBuilder builder,
        CancellationToken ct)
    {
        var charge = await readers.ReadReceivableChargeHeadAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);

        var reg = await registers.GetByIdAsync(policy.ReceivablesOpenItemsOperationalRegisterId, ct);
        if (reg is null)
            throw new NgbConfigurationViolationException(
                $"Operational register '{policy.ReceivablesOpenItemsOperationalRegisterId}' referenced by '{PropertyManagementCodes.AccountingPolicy}' was not found.");

        var occurredAtUtc = new DateTime(charge.DueOnUtc.Year, charge.DueOnUtc.Month, charge.DueOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");
        var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");

        var bag = new DimensionBag([
            new DimensionValue(partyDimId, charge.PartyId),
            new DimensionValue(propertyDimId, charge.PropertyId),
            new DimensionValue(leaseDimId, charge.LeaseId),
            new DimensionValue(itemDimId, document.Id)
        ]);

        var dimensionSetId = await dimensionSets.GetOrCreateIdAsync(bag, ct);

        builder.Add(
            registerCode: reg.Code,
            new OperationalRegisterMovement(
                DocumentId: document.Id,
                OccurredAtUtc: occurredAtUtc,
                DimensionSetId: dimensionSetId,
                Resources: new Dictionary<string, decimal> { ["amount"] = charge.Amount }));
    }
}
