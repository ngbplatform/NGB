using NGB.Accounting.Posting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Accounting posting for pm.payable_payment.
///
/// Semantics:
/// - OccurredAtUtc = paid_on_utc (00:00:00Z)
/// - Dr AP Vendors / Cr Cash
/// - Dimensions on AP side: (pm.party, pm.property)
/// - Creates standalone unapplied vendor credit; allocation happens in pm.payable_apply.
/// </summary>
public sealed class PayablePaymentPostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader,
    IPropertyManagementBankAccountReader bankAccounts)
    : IDocumentPostingHandler
{
    public string TypeCode => PropertyManagementCodes.PayablePayment;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var payment = await readers.ReadPayablePaymentHeadAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var coa = await ctx.GetChartOfAccountsAsync(ct);
        var cashAccountId = await ResolveCashAccountIdAsync(payment.BankAccountId, policy.CashAccountId, ct);
        var debit = coa.Get(policy.AccountsPayableVendorsAccountId);
        var credit = coa.Get(cashAccountId);

        var period = new DateTime(payment.PaidOnUtc.Year, payment.PaidOnUtc.Month, payment.PaidOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");

        var apBag = new DimensionBag([
            new DimensionValue(partyDimId, payment.PartyId),
            new DimensionValue(propertyDimId, payment.PropertyId)
        ]);

        ctx.Post(
            documentId: document.Id,
            period: period,
            debit: debit,
            credit: credit,
            amount: payment.Amount,
            debitDimensions: apBag,
            creditDimensions: DimensionBag.Empty);
    }

    private async Task<Guid> ResolveCashAccountIdAsync(
        Guid? selectedBankAccountId,
        Guid fallbackCashAccountId,
        CancellationToken ct)
    {
        if (selectedBankAccountId is { } bankAccountId)
        {
            var selected = await bankAccounts.GetRequiredAsync(bankAccountId, ct);
            if (selected.IsDeleted)
                throw PayablePaymentValidationException.BankAccountDeleted(bankAccountId);

            return selected.GlAccountId;
        }

        var defaultBankAccount = await bankAccounts.TryGetDefaultAsync(ct);
        return defaultBankAccount?.GlAccountId ?? fallbackCashAccountId;
    }
}
