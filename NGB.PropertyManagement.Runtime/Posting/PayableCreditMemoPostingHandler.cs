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
/// Accounting posting for pm.payable_credit_memo.
///
/// Semantics:
/// - OccurredAtUtc = credited_on_utc (00:00:00Z)
/// - Dr AP Vendors / Cr Expense-or-offset account resolved from pm.payable_charge_type.debit_account_id
/// </summary>
public sealed class PayableCreditMemoPostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader)
    : IDocumentPostingHandler
{
    public string TypeCode => PropertyManagementCodes.PayableCreditMemo;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var memo = await readers.ReadPayableCreditMemoHeadAsync(document.Id, ct);
        var chargeType = await readers.ReadPayableChargeTypeHeadAsync(memo.ChargeTypeId, ct);
        var policy = await policyReader.GetRequiredAsync(ct);

        if (chargeType.DebitAccountId is null || chargeType.DebitAccountId == Guid.Empty)
        {
            throw new NgbConfigurationViolationException(
                $"Payable charge type '{memo.ChargeTypeId}' is missing debit_account_id.",
                context: new Dictionary<string, object?>
                {
                    ["catalogType"] = PropertyManagementCodes.PayableChargeType,
                    ["chargeTypeId"] = memo.ChargeTypeId,
                    ["field"] = "debit_account_id"
                });
        }

        if (memo.Amount <= 0m)
            throw PayableCreditMemoValidationException.AmountMustBePositive(memo.Amount, document.Id);

        var coa = await ctx.GetChartOfAccountsAsync(ct);
        var debit = coa.Get(policy.AccountsPayableVendorsAccountId);
        var credit = coa.Get(chargeType.DebitAccountId.Value);
        var period = new DateTime(memo.CreditedOnUtc.Year, memo.CreditedOnUtc.Month, memo.CreditedOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");

        var bag = new DimensionBag([
            new DimensionValue(partyDimId, memo.PartyId),
            new DimensionValue(propertyDimId, memo.PropertyId)
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
