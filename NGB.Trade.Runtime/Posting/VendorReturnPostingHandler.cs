using NGB.Accounting.Posting;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.Trade.Documents;
using NGB.Trade.Runtime.Policy;

namespace NGB.Trade.Runtime.Posting;

public sealed class VendorReturnPostingHandler(
    ITradeDocumentReaders readers,
    ITradeAccountingPolicyReader policyReader)
    : IDocumentPostingHandler
{
    public string TypeCode => TradeCodes.VendorReturn;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var head = await readers.ReadVendorReturnHeadAsync(document.Id, ct);
        var lines = await readers.ReadVendorReturnLinesAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var chart = await ctx.GetChartOfAccountsAsync(ct);

        var accountsPayable = chart.Get(policy.AccountsPayableAccountId);
        var inventory = chart.Get(policy.InventoryAccountId);
        var payableBag = TradePostingCommon.PartyBag(head.VendorId);
        var occurredAtUtc = TradePostingCommon.ToOccurredAtUtc(head.DocumentDateUtc);

        foreach (var line in lines)
        {
            var inventoryBag = TradePostingCommon.InventoryBag(line.ItemId, head.WarehouseId);

            ctx.Post(
                documentId: document.Id,
                period: occurredAtUtc,
                debit: accountsPayable,
                credit: inventory,
                amount: line.LineAmount,
                debitDimensions: payableBag,
                creditDimensions: inventoryBag);
        }
    }
}
