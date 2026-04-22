using NGB.Accounting.Posting;
using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.Runtime.Policy;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;

namespace NGB.AgencyBilling.Runtime.Posting;

public sealed class SalesInvoicePostingHandler(
    IAgencyBillingDocumentReaders readers,
    IAgencyBillingAccountingPolicyReader policyReader)
    : IDocumentPostingHandler
{
    public string TypeCode => AgencyBillingCodes.SalesInvoice;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var head = await readers.ReadSalesInvoiceHeadAsync(document.Id, ct);
        var lines = await readers.ReadSalesInvoiceLinesAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var chart = await ctx.GetChartOfAccountsAsync(ct);

        var accountsReceivable = chart.Get(policy.AccountsReceivableAccountId);
        var serviceRevenue = chart.Get(policy.ServiceRevenueAccountId);
        var amount = lines.Count == 0
            ? AgencyBillingPostingCommon.RoundScale4(head.Amount)
            : AgencyBillingPostingCommon.RoundScale4(lines.Sum(AgencyBillingPostingCommon.ResolveSalesInvoiceLineAmount));

        if (amount <= 0m)
            return;

        var bag = AgencyBillingPostingCommon.ProjectBag(head.ClientId, head.ProjectId);
        var occurredAtUtc = AgencyBillingPostingCommon.ToOccurredAtUtc(head.DocumentDateUtc);

        ctx.Post(
            documentId: document.Id,
            period: occurredAtUtc,
            debit: accountsReceivable,
            credit: serviceRevenue,
            amount: amount,
            debitDimensions: bag,
            creditDimensions: bag);
    }
}
