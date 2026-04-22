using NGB.Accounting.Posting;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.Trade.Documents;
using NGB.Trade.Runtime.Policy;

namespace NGB.Trade.Runtime.Posting;

public sealed class InventoryAdjustmentPostingHandler(
    ITradeDocumentReaders readers,
    ITradeAccountingPolicyReader policyReader)
    : IDocumentPostingHandler
{
    public string TypeCode => TradeCodes.InventoryAdjustment;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var head = await readers.ReadInventoryAdjustmentHeadAsync(document.Id, ct);
        var lines = await readers.ReadInventoryAdjustmentLinesAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var chart = await ctx.GetChartOfAccountsAsync(ct);

        var inventory = chart.Get(policy.InventoryAccountId);
        var adjustment = chart.Get(policy.InventoryAdjustmentAccountId);
        var occurredAtUtc = TradePostingCommon.ToOccurredAtUtc(head.DocumentDateUtc);

        foreach (var line in lines)
        {
            var inventoryBag = TradePostingCommon.InventoryBag(line.ItemId, head.WarehouseId);

            if (line.QuantityDelta > 0m)
            {
                ctx.Post(
                    documentId: document.Id,
                    period: occurredAtUtc,
                    debit: inventory,
                    credit: adjustment,
                    amount: line.LineAmount,
                    debitDimensions: inventoryBag,
                    creditDimensions: inventoryBag);
            }
            else
            {
                ctx.Post(
                    documentId: document.Id,
                    period: occurredAtUtc,
                    debit: adjustment,
                    credit: inventory,
                    amount: line.LineAmount,
                    debitDimensions: inventoryBag,
                    creditDimensions: inventoryBag);
            }
        }
    }
}
