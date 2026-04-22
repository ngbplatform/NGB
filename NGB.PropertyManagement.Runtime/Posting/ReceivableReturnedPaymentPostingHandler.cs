using NGB.Accounting.Posting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Accounting posting for pm.receivable_returned_payment.
///
/// Semantics:
/// - OccurredAtUtc = returned_on_utc (00:00:00Z)
/// - Dr AR Tenants / Cr Cash
/// - Dimensions on AR side: (pm.party, pm.property, pm.lease)
///
/// This document models a bounced/reversed tenant receipt.
/// It does not create a new charge item. Instead it reverses the original payment's available credit
/// through the receivables open-items register.
/// </summary>
public sealed class ReceivableReturnedPaymentPostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader,
    IPropertyManagementBankAccountReader bankAccounts)
    : IDocumentPostingHandler
{
    public string TypeCode => PropertyManagementCodes.ReceivableReturnedPayment;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var returned = await readers.ReadReceivableReturnedPaymentHeadAsync(document.Id, ct);
        await LeaseConsistencyGuard.EnsureAsync(document.Id, returned.LeaseId, returned.PartyId, returned.PropertyId, readers, ct);
        var policy = await policyReader.GetRequiredAsync(ct);

        var coa = await ctx.GetChartOfAccountsAsync(ct);
        var cashAccountId = await ResolveCashAccountIdAsync(returned.BankAccountId, policy.CashAccountId, ct);
        var debit = coa.Get(policy.AccountsReceivableTenantsAccountId);
        var credit = coa.Get(cashAccountId);

        var period = new DateTime(returned.ReturnedOnUtc.Year, returned.ReturnedOnUtc.Month, returned.ReturnedOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");

        var arBag = new DimensionBag([
            new DimensionValue(partyDimId, returned.PartyId),
            new DimensionValue(propertyDimId, returned.PropertyId),
            new DimensionValue(leaseDimId, returned.LeaseId)
        ]);

        ctx.Post(
            documentId: document.Id,
            period: period,
            debit: debit,
            credit: credit,
            amount: returned.Amount,
            debitDimensions: arBag,
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
                throw ReceivableReturnedPaymentValidationException.BankAccountDeleted(bankAccountId);

            return selected.GlAccountId;
        }

        var defaultBankAccount = await bankAccounts.TryGetDefaultAsync(ct);
        return defaultBankAccount?.GlAccountId ?? fallbackCashAccountId;
    }
}
