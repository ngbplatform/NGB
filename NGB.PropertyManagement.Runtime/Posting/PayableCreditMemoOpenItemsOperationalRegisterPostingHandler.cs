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
/// Operational Registers posting for pm.payable_credit_memo into payables open-items register.
///
/// Semantics:
/// - Writes -amount as standalone unapplied vendor credit.
/// - pm.payable_item valueId = credit memo document id.
/// - Allocation into payable charges will be handled later by pm.payable_apply generalized model.
/// </summary>
public sealed class PayableCreditMemoOpenItemsOperationalRegisterPostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader,
    IOperationalRegisterRepository registers,
    IDimensionSetService dimensionSets)
    : IDocumentOperationalRegisterPostingHandler
{
    public string TypeCode => PropertyManagementCodes.PayableCreditMemo;

    public async Task BuildMovementsAsync(
        DocumentRecord document,
        IOperationalRegisterMovementsBuilder builder,
        CancellationToken ct)
    {
        var memo = await readers.ReadPayableCreditMemoHeadAsync(document.Id, ct);
        if (memo.Amount <= 0m)
            throw PayableCreditMemoValidationException.AmountMustBePositive(memo.Amount, document.Id);

        var policy = await policyReader.GetRequiredAsync(ct);
        var reg = await registers.GetByIdAsync(policy.PayablesOpenItemsOperationalRegisterId, ct);
        if (reg is null)
            throw new NgbConfigurationViolationException(
                $"Operational register '{policy.PayablesOpenItemsOperationalRegisterId}' referenced by '{PropertyManagementCodes.AccountingPolicy}' was not found.");

        var occurredAtUtc = new DateTime(memo.CreditedOnUtc.Year, memo.CreditedOnUtc.Month, memo.CreditedOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.PayableItem}");

        var bag = new DimensionBag([
            new DimensionValue(partyDimId, memo.PartyId),
            new DimensionValue(propertyDimId, memo.PropertyId),
            new DimensionValue(itemDimId, document.Id)
        ]);

        var dimensionSetId = await dimensionSets.GetOrCreateIdAsync(bag, ct);

        builder.Add(
            registerCode: reg.Code,
            new OperationalRegisterMovement(
                DocumentId: document.Id,
                OccurredAtUtc: occurredAtUtc,
                DimensionSetId: dimensionSetId,
                Resources: new Dictionary<string, decimal> { ["amount"] = -memo.Amount }));
    }
}
