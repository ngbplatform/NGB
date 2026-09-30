using NGB.Accounting.Posting;
using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.Runtime.Policy;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Posting;

public sealed class CustomerPaymentPostingHandler(
    IAgencyBillingDocumentReaders readers,
    IAgencyBillingAccountingPolicyReader policyReader)
    : IDocumentPostingHandler
{
    public string TypeCode => AgencyBillingCodes.CustomerPayment;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var payment = await readers.ReadCustomerPaymentHeadAsync(document.Id, ct);
        var applies = await readers.ReadCustomerPaymentAppliesAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var chart = await ctx.GetChartOfAccountsAsync(ct);

        var cash = chart.Get(payment.CashAccountId ?? policy.CashAccountId);
        var accountsReceivable = chart.Get(policy.AccountsReceivableAccountId);
        var occurredAtUtc = AgencyBillingPostingCommon.ToOccurredAtUtc(payment.DocumentDateUtc);
        var invoiceHeads = await readers.ReadSalesInvoiceHeadsAsync(
            applies.Select(static apply => apply.SalesInvoiceId).Distinct().ToArray(),
            ct);

        foreach (var apply in applies)
        {
            if (!invoiceHeads.TryGetValue(apply.SalesInvoiceId, out var invoice))
                throw new NgbInvariantViolationException($"Sales Invoice '{apply.SalesInvoiceId}' is missing its Agency Billing head row.");

            var amount = AgencyBillingPostingCommon.RoundScale4(apply.AppliedAmount);
            if (amount <= 0m)
                continue;

            ctx.Post(
                documentId: document.Id,
                period: occurredAtUtc,
                debit: cash,
                credit: accountsReceivable,
                amount: amount,
                debitDimensions: DimensionBag.Empty,
                creditDimensions: AgencyBillingPostingCommon.ProjectBag(invoice.ClientId, invoice.ProjectId));
        }
    }
}
