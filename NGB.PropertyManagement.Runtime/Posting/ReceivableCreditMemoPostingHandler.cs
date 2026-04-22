using NGB.Accounting.Posting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Accounting posting for pm.receivable_credit_memo.
///
/// Semantics:
/// - OccurredAtUtc = credited_on_utc (00:00:00Z)
/// - Dr selected income/revenue reversal account / Cr AR Tenants
/// - Dimensions: (pm.party, pm.property, pm.lease) on both sides
/// - Classification is explicit via required charge_type_id.
/// </summary>
public sealed class ReceivableCreditMemoPostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader)
    : IDocumentPostingHandler
{
    public string TypeCode => PropertyManagementCodes.ReceivableCreditMemo;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var memo = await readers.ReadReceivableCreditMemoHeadAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var coa = await ctx.GetChartOfAccountsAsync(ct);
        var credit = coa.Get(policy.AccountsReceivableTenantsAccountId);

        if (memo.ChargeTypeId is null)
            throw ReceivableCreditMemoValidationException.ClassificationRequired(memo.DocumentId);

        var chargeType = await readers.ReadChargeTypeHeadAsync(memo.ChargeTypeId.Value, ct);
        if (chargeType.CreditAccountId is null || chargeType.CreditAccountId == Guid.Empty)
        {
            throw new NgbConfigurationViolationException(
                $"Charge type '{chargeType.ChargeTypeId}' is missing credit_account_id.",
                context: new Dictionary<string, object?>
                {
                    ["catalogType"] = PropertyManagementCodes.ReceivableChargeType,
                    ["chargeTypeId"] = chargeType.ChargeTypeId,
                    ["field"] = "credit_account_id"
                });
        }

        var debit = coa.Get(chargeType.CreditAccountId.Value);
        var period = new DateTime(memo.CreditedOnUtc.Year, memo.CreditedOnUtc.Month, memo.CreditedOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");

        var bag = new DimensionBag([
            new DimensionValue(partyDimId, memo.PartyId),
            new DimensionValue(propertyDimId, memo.PropertyId),
            new DimensionValue(leaseDimId, memo.LeaseId)
        ]);

        ctx.Post(
            documentId: document.Id,
            period: period,
            debit: debit,
            credit: credit,
            amount: memo.Amount,
            debitDimensions: bag,
            creditDimensions: bag);
    }
}
