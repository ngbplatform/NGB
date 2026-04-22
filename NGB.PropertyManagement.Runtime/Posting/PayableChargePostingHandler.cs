using NGB.Accounting.Posting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Accounting posting for pm.payable_charge.
///
/// Semantics:
/// - OccurredAtUtc = due_on_utc (00:00:00Z)
/// - Dr Expense (from pm.payable_charge_type.debit_account_id) / Cr AP Vendors (policy)
/// - Dimensions: (pm.party, pm.property)
/// </summary>
public sealed class PayableChargePostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader)
    : IDocumentPostingHandler
{
    public string TypeCode => PropertyManagementCodes.PayableCharge;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var charge = await readers.ReadPayableChargeHeadAsync(document.Id, ct);
        var chargeType = await readers.ReadPayableChargeTypeHeadAsync(charge.ChargeTypeId, ct);
        var policy = await policyReader.GetRequiredAsync(ct);

        if (chargeType.DebitAccountId is null || chargeType.DebitAccountId == Guid.Empty)
        {
            throw new NgbConfigurationViolationException(
                $"Payable charge type '{charge.ChargeTypeId}' is missing debit_account_id.",
                context: new Dictionary<string, object?>
                {
                    ["catalogType"] = PropertyManagementCodes.PayableChargeType,
                    ["chargeTypeId"] = charge.ChargeTypeId,
                    ["field"] = "debit_account_id"
                });
        }

        var coa = await ctx.GetChartOfAccountsAsync(ct);
        var debit = coa.Get(chargeType.DebitAccountId.Value);
        var credit = coa.Get(policy.AccountsPayableVendorsAccountId);

        var period = new DateTime(charge.DueOnUtc.Year, charge.DueOnUtc.Month, charge.DueOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");

        var bag = new DimensionBag([
            new DimensionValue(partyDimId, charge.PartyId),
            new DimensionValue(propertyDimId, charge.PropertyId)
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
