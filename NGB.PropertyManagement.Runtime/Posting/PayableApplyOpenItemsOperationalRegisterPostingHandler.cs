using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Payables;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Operational Registers posting for pm.payable_apply.
///
/// Semantics:
/// - No accounting entries.
/// - Writes two movements into payables open items register:
///   - charge item: -amount
///   - credit source item: +amount
/// </summary>
public sealed class PayableApplyOpenItemsOperationalRegisterPostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceNetReader netReader,
    IDimensionSetService dimensionSets,
    IDocumentRelationshipService relationships,
    IDocumentRepository documents,
    IAdvisoryLockManager locks)
    : IDocumentOperationalRegisterPostingHandler
{
    public string TypeCode => PropertyManagementCodes.PayableApply;

    public async Task BuildMovementsAsync(
        DocumentRecord document,
        IOperationalRegisterMovementsBuilder builder,
        CancellationToken ct)
    {
        var apply = await readers.ReadPayableApplyHeadAsync(document.Id, ct);

        if (apply.Amount <= 0m)
            throw PayableApplyValidationException.AmountMustBePositive(apply.Amount);

        if (apply.CreditDocumentId == Guid.Empty)
            throw PayableApplyValidationException.CreditSourceRequired();

        if (apply.ChargeDocumentId == Guid.Empty)
            throw PayableApplyValidationException.ChargeRequired();

        if (apply.CreditDocumentId == apply.ChargeDocumentId)
            throw PayableApplyValidationException.CreditSourceAndChargeMustDiffer(apply.CreditDocumentId, apply.ChargeDocumentId);

        await LockTwoDocumentsAsync(locks, apply.CreditDocumentId, apply.ChargeDocumentId, ct);

        if (document.Status == DocumentStatus.Draft)
        {
            await PayablesApplyExecutionHelpers.EnsureApplyRelationshipsAsync(
                relationships,
                applyId: document.Id,
                creditDocumentId: apply.CreditDocumentId,
                chargeDocumentId: apply.ChargeDocumentId,
                ct: ct);
        }

        var creditSource = await PayableCreditSourceResolver.ReadRequiredAsync(readers, documents, apply.CreditDocumentId, ct);
        var charge = await readers.ReadPayableChargeHeadAsync(apply.ChargeDocumentId, ct);

        if (creditSource.PartyId != charge.PartyId || creditSource.PropertyId != charge.PropertyId)
            throw PayableApplyValidationException.PartyPropertyMismatch(apply.CreditDocumentId, apply.ChargeDocumentId);

        var policy = await policyReader.GetRequiredAsync(ct);
        var reg = await registers.GetByIdAsync(policy.PayablesOpenItemsOperationalRegisterId, ct);
        if (reg is null)
            throw new NgbConfigurationViolationException(
                $"Operational register '{policy.PayablesOpenItemsOperationalRegisterId}' referenced by '{PropertyManagementCodes.AccountingPolicy}' was not found.");

        var occurredAtUtc = new DateTime(apply.AppliedOnUtc.Year, apply.AppliedOnUtc.Month, apply.AppliedOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.PayableItem}");

        var chargeBag = new DimensionBag([
            new DimensionValue(partyDimId, charge.PartyId),
            new DimensionValue(propertyDimId, charge.PropertyId),
            new DimensionValue(itemDimId, apply.ChargeDocumentId)
        ]);

        var creditBag = new DimensionBag([
            new DimensionValue(partyDimId, creditSource.PartyId),
            new DimensionValue(propertyDimId, creditSource.PropertyId),
            new DimensionValue(itemDimId, apply.CreditDocumentId)
        ]);

        var chargeDimSetId = await dimensionSets.GetOrCreateIdAsync(chargeBag, ct);
        var creditDimSetId = await dimensionSets.GetOrCreateIdAsync(creditBag, ct);

        var chargeOutstanding = await netReader.GetNetByDimensionSetAsync(reg.RegisterId, chargeDimSetId, resourceColumnCode: "amount", ct);
        var creditNet = await netReader.GetNetByDimensionSetAsync(reg.RegisterId, creditDimSetId, resourceColumnCode: "amount", ct);

        if (document.Status == DocumentStatus.Posted)
        {
            chargeOutstanding += apply.Amount;
            creditNet -= apply.Amount;
        }

        var availableCredit = creditNet >= 0m ? 0m : -creditNet;

        if (chargeOutstanding < apply.Amount)
            throw PayableApplyValidationException.OverApplyCharge(apply.ChargeDocumentId, apply.Amount, chargeOutstanding);

        if (availableCredit < apply.Amount)
            throw PayableApplyValidationException.InsufficientCredit(apply.CreditDocumentId, apply.Amount, availableCredit);

        builder.Add(
            registerCode: reg.Code,
            new OperationalRegisterMovement(
                DocumentId: document.Id,
                OccurredAtUtc: occurredAtUtc,
                DimensionSetId: chargeDimSetId,
                Resources: new Dictionary<string, decimal> { ["amount"] = -apply.Amount }));

        builder.Add(
            registerCode: reg.Code,
            new OperationalRegisterMovement(
                DocumentId: document.Id,
                OccurredAtUtc: occurredAtUtc,
                DimensionSetId: creditDimSetId,
                Resources: new Dictionary<string, decimal> { ["amount"] = apply.Amount }));
    }

    private static async Task LockTwoDocumentsAsync(IAdvisoryLockManager locks, Guid a, Guid b, CancellationToken ct)
    {
        if (a.CompareTo(b) < 0)
        {
            await locks.LockDocumentAsync(a, ct);
            await locks.LockDocumentAsync(b, ct);
            return;
        }

        await locks.LockDocumentAsync(b, ct);
        await locks.LockDocumentAsync(a, ct);
    }
}
