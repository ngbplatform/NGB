using NGB.Accounting.Posting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Accounting posting for pm.receivable_charge (open-item AR).
///
/// Semantics:
/// - OccurredAtUtc = due_on_utc (00:00:00Z)
/// - Dr AR Tenants (policy) / Cr Income (from pm.charge_type.credit_account_id)
/// - Dimensions: (pm.party, pm.property, pm.lease)
/// </summary>
public sealed class ReceivableChargePostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader)
    : IDocumentPostingHandler
{
    public string TypeCode => PropertyManagementCodes.ReceivableCharge;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var charge = await readers.ReadReceivableChargeHeadAsync(document.Id, ct);
        await LeaseConsistencyGuard.EnsureAsync(document.Id, charge.LeaseId, charge.PartyId, charge.PropertyId, readers, ct);
        var chargeType = await readers.ReadChargeTypeHeadAsync(charge.ChargeTypeId, ct);
        var policy = await policyReader.GetRequiredAsync(ct);

        if (chargeType.CreditAccountId is null || chargeType.CreditAccountId == Guid.Empty)
        {
            throw new NgbConfigurationViolationException(
                $"Charge type '{charge.ChargeTypeId}' is missing credit_account_id.",
                context: new Dictionary<string, object?>
                {
                    ["catalogType"] = PropertyManagementCodes.ReceivableChargeType,
                    ["chargeTypeId"] = charge.ChargeTypeId,
                    ["field"] = "credit_account_id"
                });
        }

        var coa = await ctx.GetChartOfAccountsAsync(ct);
        var debit = coa.Get(policy.AccountsReceivableTenantsAccountId);
        var credit = coa.Get(chargeType.CreditAccountId.Value);

        var period = new DateTime(charge.DueOnUtc.Year, charge.DueOnUtc.Month, charge.DueOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");

        var bag = new DimensionBag([
            new DimensionValue(partyDimId, charge.PartyId),
            new DimensionValue(propertyDimId, charge.PropertyId),
            new DimensionValue(leaseDimId, charge.LeaseId)
        ]);

        ctx.Post(
            documentId: document.Id,
            period: period,
            debit: debit,
            credit: credit,
            amount: charge.Amount,
            debitDimensions: bag,
            creditDimensions: bag);
    }
}
