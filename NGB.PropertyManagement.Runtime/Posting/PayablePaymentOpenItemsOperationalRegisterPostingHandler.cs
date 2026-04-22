using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Dimensions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Operational Registers posting for pm.payable_payment into the payables open-items register.
///
/// Semantics:
/// - Writes -amount as standalone unapplied vendor credit.
/// - pm.payable_item valueId = payment document id.
/// - Allocation into payable charges is done by pm.payable_apply.
/// </summary>
public sealed class PayablePaymentOpenItemsOperationalRegisterPostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader,
    IOperationalRegisterRepository registers,
    IDimensionSetService dimensionSets)
    : IDocumentOperationalRegisterPostingHandler
{
    public string TypeCode => PropertyManagementCodes.PayablePayment;

    public async Task BuildMovementsAsync(
        DocumentRecord document,
        IOperationalRegisterMovementsBuilder builder,
        CancellationToken ct)
    {
        var payment = await readers.ReadPayablePaymentHeadAsync(document.Id, ct);
        if (payment.Amount <= 0m)
            throw PayablePaymentValidationException.AmountMustBePositive(payment.Amount, document.Id);

        var policy = await policyReader.GetRequiredAsync(ct);
        var reg = await registers.GetByIdAsync(policy.PayablesOpenItemsOperationalRegisterId, ct);
        if (reg is null)
            throw new NgbConfigurationViolationException(
                $"Operational register '{policy.PayablesOpenItemsOperationalRegisterId}' referenced by '{PropertyManagementCodes.AccountingPolicy}' was not found.");

        var occurredAtUtc = new DateTime(payment.PaidOnUtc.Year, payment.PaidOnUtc.Month, payment.PaidOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.PayableItem}");

        var bag = new DimensionBag([
            new DimensionValue(partyDimId, payment.PartyId),
            new DimensionValue(propertyDimId, payment.PropertyId),
            new DimensionValue(itemDimId, document.Id)
        ]);

        var dimensionSetId = await dimensionSets.GetOrCreateIdAsync(bag, ct);

        builder.Add(
            registerCode: reg.Code,
            new OperationalRegisterMovement(
                DocumentId: document.Id,
                OccurredAtUtc: occurredAtUtc,
                DimensionSetId: dimensionSetId,
                Resources: new Dictionary<string, decimal> { ["amount"] = -payment.Amount }));
    }
}
