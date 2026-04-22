using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.Persistence.Documents;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Operational Registers posting for pm.receivable_apply into the receivables open-items register.
///
/// Semantics:
/// - Apply allocates credit from a payment item to a charge item.
/// - No accounting entries are created (GL impact already comes from charge + payment).
/// - Writes two movements into policy.ReceivablesOpenItemsOperationalRegisterId:
///   - charge item: -amount
///   - payment credit item: +amount
///
/// Validations:
/// - amount must be positive
/// - payment and charge must belong to the same (party, property, lease)
/// - cannot apply more than charge outstanding
/// - cannot apply more than available credit
///
/// Concurrency:
/// - acquires advisory locks on both referenced documents (charge_document_id and credit_document_id)
///   to prevent concurrent applications that could over-apply.
/// </summary>
public sealed class ReceivableApplyOpenItemsOperationalRegisterPostingHandler(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents,
    IPropertyManagementAccountingPolicyReader policyReader,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceNetReader netReader,
    IDimensionSetService dimensionSets,
    IDocumentRelationshipService relationships,
    IAdvisoryLockManager locks)
    : IDocumentOperationalRegisterPostingHandler
{
    public string TypeCode => PropertyManagementCodes.ReceivableApply;

    public async Task BuildMovementsAsync(
        DocumentRecord document,
        IOperationalRegisterMovementsBuilder builder,
        CancellationToken ct)
    {
        var apply = await readers.ReadReceivableApplyHeadAsync(document.Id, ct);

        if (apply.Amount <= 0m)
            throw ReceivableApplyValidationException.AmountMustBePositive(apply.Amount);

        if (apply.CreditDocumentId == Guid.Empty)
            throw ReceivableApplyValidationException.CreditSourceRequired();

        if (apply.ChargeDocumentId == Guid.Empty)
            throw ReceivableApplyValidationException.ChargeRequired();

        if (apply.CreditDocumentId == apply.ChargeDocumentId)
            throw ReceivableApplyValidationException.PaymentAndChargeMustMatch(apply.CreditDocumentId, apply.ChargeDocumentId);

        // Prevent concurrent apply races (and concurrent unpost/repost of those docs).
        await LockTwoDocumentsAsync(locks, apply.CreditDocumentId, apply.ChargeDocumentId, ct);

        // Relationships for apply are draft-only mutations. Create them on first post when the
        // document is still Draft, but never try to re-create them during repost.
        if (document.Status == DocumentStatus.Draft)
        {
            await ReceivablesApplyExecutionHelpers.EnsureApplyRelationshipsAsync(
                relationships,
                applyId: document.Id,
                creditDocumentId: apply.CreditDocumentId,
                chargeDocumentId: apply.ChargeDocumentId,
                ct: ct);
        }

        var creditSource = await ReceivableCreditSourceResolver.ReadRequiredAsync(readers, documents, apply.CreditDocumentId, ct);
        var charge = await ReadChargeLikeContextAsync(apply.ChargeDocumentId, ct);

        if (creditSource.PartyId != charge.PartyId
            || creditSource.PropertyId != charge.PropertyId
            || creditSource.LeaseId != charge.LeaseId)
        {
            throw ReceivableApplyValidationException.PartyPropertyLeaseMismatch(apply.CreditDocumentId, apply.ChargeDocumentId);
        }

        var policy = await policyReader.GetRequiredAsync(ct);
        var reg = await registers.GetByIdAsync(policy.ReceivablesOpenItemsOperationalRegisterId, ct);
        if (reg is null)
            throw new NgbConfigurationViolationException(
                $"Operational register '{policy.ReceivablesOpenItemsOperationalRegisterId}' referenced by '{PropertyManagementCodes.AccountingPolicy}' was not found.");

        var occurredAtUtc = new DateTime(apply.AppliedOnUtc.Year, apply.AppliedOnUtc.Month, apply.AppliedOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");
        var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");

        var chargeBag = new DimensionBag([
            new DimensionValue(partyDimId, charge.PartyId),
            new DimensionValue(propertyDimId, charge.PropertyId),
            new DimensionValue(leaseDimId, charge.LeaseId),
            new DimensionValue(itemDimId, apply.ChargeDocumentId)
        ]);

        var paymentBag = new DimensionBag([
            new DimensionValue(partyDimId, creditSource.PartyId),
            new DimensionValue(propertyDimId, creditSource.PropertyId),
            new DimensionValue(leaseDimId, creditSource.LeaseId),
            new DimensionValue(itemDimId, apply.CreditDocumentId)
        ]);

        var chargeDimSetId = await dimensionSets.GetOrCreateIdAsync(chargeBag, ct);
        var paymentDimSetId = await dimensionSets.GetOrCreateIdAsync(paymentBag, ct);

        var chargeOutstanding = await netReader.GetNetByDimensionSetAsync(reg.RegisterId, chargeDimSetId, resourceColumnCode: "amount", ct);
        var paymentNet = await netReader.GetNetByDimensionSetAsync(reg.RegisterId, paymentDimSetId, resourceColumnCode: "amount", ct);

        // During repost, the current posted apply is still present in register state while we are
        // building the fresh movement set. Validate against the effective pre-apply state by
        // adding back this document's own previous effect.
        if (document.Status == DocumentStatus.Posted)
        {
            chargeOutstanding += apply.Amount;
            paymentNet -= apply.Amount;
        }

        var availableCredit = paymentNet >= 0m ? 0m : -paymentNet;

        if (chargeOutstanding < apply.Amount)
            throw ReceivableApplyValidationException.OverApplyCharge(apply.ChargeDocumentId, apply.Amount, chargeOutstanding);

        if (availableCredit < apply.Amount)
            throw ReceivableApplyValidationException.InsufficientCredit(apply.CreditDocumentId, apply.Amount, availableCredit);

        // Allocation movements.
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
                DimensionSetId: paymentDimSetId,
                Resources: new Dictionary<string, decimal> { ["amount"] = apply.Amount }));
    }

    private async Task<ChargeLikeContext> ReadChargeLikeContextAsync(Guid chargeDocumentId, CancellationToken ct)
    {
        try
        {
            var charge = await readers.ReadReceivableChargeHeadAsync(chargeDocumentId, ct);
            return new ChargeLikeContext(charge.PartyId, charge.PropertyId, charge.LeaseId);
        }
        catch (InvalidOperationException)
        {
            // try next charge-like type
        }

        try
        {
            var lateFee = await readers.ReadLateFeeChargeHeadAsync(chargeDocumentId, ct);
            return new ChargeLikeContext(lateFee.PartyId, lateFee.PropertyId, lateFee.LeaseId);
        }
        catch (InvalidOperationException)
        {
            // try next charge-like type
        }

        var rent = await readers.ReadRentChargeHeadAsync(chargeDocumentId, ct);
        return new ChargeLikeContext(rent.PartyId, rent.PropertyId, rent.LeaseId);
    }

    private readonly record struct ChargeLikeContext(Guid PartyId, Guid PropertyId, Guid LeaseId);

    private static async Task LockTwoDocumentsAsync(IAdvisoryLockManager locks, Guid a, Guid b, CancellationToken ct)
    {
        // Deterministic ordering to avoid deadlocks.
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
