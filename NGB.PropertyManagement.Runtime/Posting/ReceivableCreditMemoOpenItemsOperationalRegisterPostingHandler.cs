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
/// Operational Registers posting for pm.receivable_credit_memo into the receivables open-items register.
///
/// Semantics:
/// - Writes -amount to the credit memo's own item (pm.receivable_item = document.Id).
/// - Credit memo is a standalone credit source that is later settled by pm.receivable_apply.
/// </summary>
public sealed class ReceivableCreditMemoOpenItemsOperationalRegisterPostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader,
    IOperationalRegisterRepository registers,
    IDimensionSetService dimensionSets)
    : IDocumentOperationalRegisterPostingHandler
{
    public string TypeCode => PropertyManagementCodes.ReceivableCreditMemo;

    public async Task BuildMovementsAsync(
        DocumentRecord document,
        IOperationalRegisterMovementsBuilder builder,
        CancellationToken ct)
    {
        var memo = await readers.ReadReceivableCreditMemoHeadAsync(document.Id, ct);
        if (memo.Amount <= 0m)
            throw ReceivableCreditMemoValidationException.AmountMustBePositive(memo.Amount, document.Id);

        var policy = await policyReader.GetRequiredAsync(ct);
        var reg = await registers.GetByIdAsync(policy.ReceivablesOpenItemsOperationalRegisterId, ct);
        if (reg is null)
            throw new NgbConfigurationViolationException(
                $"Operational register '{policy.ReceivablesOpenItemsOperationalRegisterId}' referenced by '{PropertyManagementCodes.AccountingPolicy}' was not found.");

        var occurredAtUtc = new DateTime(memo.CreditedOnUtc.Year, memo.CreditedOnUtc.Month, memo.CreditedOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");
        var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");

        var creditBag = new DimensionBag([
            new DimensionValue(partyDimId, memo.PartyId),
            new DimensionValue(propertyDimId, memo.PropertyId),
            new DimensionValue(leaseDimId, memo.LeaseId),
            new DimensionValue(itemDimId, document.Id)
        ]);

        var creditDimSetId = await dimensionSets.GetOrCreateIdAsync(creditBag, ct);

        builder.Add(
            registerCode: reg.Code,
            new OperationalRegisterMovement(
                DocumentId: document.Id,
                OccurredAtUtc: occurredAtUtc,
                DimensionSetId: creditDimSetId,
                Resources: new Dictionary<string, decimal> { ["amount"] = -memo.Amount }));
    }
}
