using NGB.Accounting.Posting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.Trade.Documents;
using NGB.Trade.Runtime.Policy;

namespace NGB.Trade.Runtime.Posting;

public sealed class CustomerPaymentPostingHandler(
    ITradeDocumentReaders readers,
    ITradeAccountingPolicyReader policyReader)
    : IDocumentPostingHandler
{
    public string TypeCode => TradeCodes.CustomerPayment;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var payment = await readers.ReadCustomerPaymentHeadAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var chart = await ctx.GetChartOfAccountsAsync(ct);

        var cash = chart.Get(payment.CashAccountId ?? policy.CashAccountId);
        var accountsReceivable = chart.Get(policy.AccountsReceivableAccountId);
        var receivableBag = TradePostingCommon.PartyBag(payment.CustomerId);
        var occurredAtUtc = TradePostingCommon.ToOccurredAtUtc(payment.DocumentDateUtc);

        ctx.Post(
            documentId: document.Id,
            period: occurredAtUtc,
            debit: cash,
            credit: accountsReceivable,
            amount: payment.Amount,
            debitDimensions: DimensionBag.Empty,
            creditDimensions: receivableBag);
    }
}
