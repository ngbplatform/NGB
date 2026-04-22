using NGB.Accounting.Posting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Accounting posting for pm.receivable_payment (open-item AR).
///
/// Semantics:
/// - OccurredAtUtc = received_on_utc (00:00:00Z)
/// - Dr Cash (policy.cash_account_id) / Cr AR Tenants (policy.ar_tenants_account_id)
/// - Dimensions:
///   - Cash side: empty bag (cash control account does not allow dimensions by default)
///   - AR side: (pm.party, pm.property, pm.lease)
///
/// Note: This document does NOT apply payment to specific charges.
/// It creates an unapplied credit open item in the receivables open-items register.
/// Applications are modeled by pm.receivable_apply.
/// </summary>
public sealed class ReceivablePaymentPostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader,
    IPropertyManagementBankAccountReader bankAccounts)
    : IDocumentPostingHandler
{
    public string TypeCode => PropertyManagementCodes.ReceivablePayment;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var payment = await readers.ReadReceivablePaymentHeadAsync(document.Id, ct);
        await LeaseConsistencyGuard.EnsureAsync(document.Id, payment.LeaseId, payment.PartyId, payment.PropertyId, readers, ct);
        var policy = await policyReader.GetRequiredAsync(ct);

        var coa = await ctx.GetChartOfAccountsAsync(ct);
        var cashAccountId = await ResolveCashAccountIdAsync(payment.BankAccountId, policy.CashAccountId, ct);
        var debit = coa.Get(cashAccountId);
        var credit = coa.Get(policy.AccountsReceivableTenantsAccountId);

        var period = new DateTime(payment.ReceivedOnUtc.Year, payment.ReceivedOnUtc.Month, payment.ReceivedOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");

        var arBag = new DimensionBag([
            new DimensionValue(partyDimId, payment.PartyId),
            new DimensionValue(propertyDimId, payment.PropertyId),
            new DimensionValue(leaseDimId, payment.LeaseId)
        ]);

        ctx.Post(
            documentId: document.Id,
            period: period,
            debit: debit,
            credit: credit,
            amount: payment.Amount,
            debitDimensions: DimensionBag.Empty,
            creditDimensions: arBag);
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
                throw ReceivablePaymentValidationException.BankAccountDeleted(bankAccountId);

            return selected.GlAccountId;
        }

        var defaultBankAccount = await bankAccounts.TryGetDefaultAsync(ct);
        return defaultBankAccount?.GlAccountId ?? fallbackCashAccountId;
    }
}
