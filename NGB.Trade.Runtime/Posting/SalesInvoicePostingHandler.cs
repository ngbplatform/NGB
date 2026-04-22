using NGB.Accounting.Posting;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.Trade.Documents;
using NGB.Trade.Runtime.Policy;

namespace NGB.Trade.Runtime.Posting;

public sealed class SalesInvoicePostingHandler(
    ITradeDocumentReaders readers,
    ITradeAccountingPolicyReader policyReader)
    : IDocumentPostingHandler
{
    public string TypeCode => TradeCodes.SalesInvoice;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var head = await readers.ReadSalesInvoiceHeadAsync(document.Id, ct);
        var lines = await readers.ReadSalesInvoiceLinesAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var chart = await ctx.GetChartOfAccountsAsync(ct);

        var accountsReceivable = chart.Get(policy.AccountsReceivableAccountId);
        var salesRevenue = chart.Get(policy.SalesRevenueAccountId);
        var cogs = chart.Get(policy.CostOfGoodsSoldAccountId);
        var inventory = chart.Get(policy.InventoryAccountId);
        var receivableBag = TradePostingCommon.PartyBag(head.CustomerId);
        var occurredAtUtc = TradePostingCommon.ToOccurredAtUtc(head.DocumentDateUtc);

        foreach (var line in lines)
        {
            var revenueBag = TradePostingCommon.SalesRevenueBag(head.CustomerId, line.ItemId, head.WarehouseId);
            var inventoryBag = TradePostingCommon.InventoryBag(line.ItemId, head.WarehouseId);
            var costAmount = TradePostingCommon.RoundScale4(line.Quantity * line.UnitCost);

            ctx.Post(
                documentId: document.Id,
                period: occurredAtUtc,
                debit: accountsReceivable,
                credit: salesRevenue,
                amount: line.LineAmount,
                debitDimensions: receivableBag,
                creditDimensions: revenueBag);

            ctx.Post(
                documentId: document.Id,
                period: occurredAtUtc,
                debit: cogs,
                credit: inventory,
                amount: costAmount,
                debitDimensions: inventoryBag,
                creditDimensions: inventoryBag);
        }
    }
}
