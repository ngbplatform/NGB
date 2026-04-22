using NGB.Accounting.Posting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.Trade.Documents;
using NGB.Trade.Runtime.Policy;

namespace NGB.Trade.Runtime.Posting;

public sealed class VendorPaymentPostingHandler(
    ITradeDocumentReaders readers,
    ITradeAccountingPolicyReader policyReader)
    : IDocumentPostingHandler
{
    public string TypeCode => TradeCodes.VendorPayment;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var payment = await readers.ReadVendorPaymentHeadAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var chart = await ctx.GetChartOfAccountsAsync(ct);

        var accountsPayable = chart.Get(policy.AccountsPayableAccountId);
        var cash = chart.Get(payment.CashAccountId ?? policy.CashAccountId);
        var payableBag = TradePostingCommon.PartyBag(payment.VendorId);
        var occurredAtUtc = TradePostingCommon.ToOccurredAtUtc(payment.DocumentDateUtc);

        ctx.Post(
            documentId: document.Id,
            period: occurredAtUtc,
            debit: accountsPayable,
            credit: cash,
            amount: payment.Amount,
            debitDimensions: payableBag,
            creditDimensions: DimensionBag.Empty);
    }
}
