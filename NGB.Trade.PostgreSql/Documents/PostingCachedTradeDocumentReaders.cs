using NGB.Persistence.Documents;
using NGB.Trade.Documents;

namespace NGB.Trade.PostgreSql.Documents;

internal sealed class PostingCachedTradeDocumentReaders(ITradeDocumentReaders inner, IDocumentPostingReadCache cache)
    : ITradeDocumentReaders
{
    public Task<TradePurchaseReceiptHead> ReadPurchaseReceiptHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadPurchaseReceiptHeadAsync), inner.ReadPurchaseReceiptHeadAsync, ct);

    public Task<IReadOnlyList<TradePurchaseReceiptLine>> ReadPurchaseReceiptLinesAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadPurchaseReceiptLinesAsync), inner.ReadPurchaseReceiptLinesAsync, ct);

    public Task<TradeSalesInvoiceHead> ReadSalesInvoiceHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadSalesInvoiceHeadAsync), inner.ReadSalesInvoiceHeadAsync, ct);

    public Task<IReadOnlyList<TradeSalesInvoiceLine>> ReadSalesInvoiceLinesAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadSalesInvoiceLinesAsync), inner.ReadSalesInvoiceLinesAsync, ct);

    public Task<TradeInventoryTransferHead> ReadInventoryTransferHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadInventoryTransferHeadAsync), inner.ReadInventoryTransferHeadAsync, ct);

    public Task<IReadOnlyList<TradeInventoryTransferLine>> ReadInventoryTransferLinesAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadInventoryTransferLinesAsync), inner.ReadInventoryTransferLinesAsync, ct);

    public Task<TradeInventoryAdjustmentHead> ReadInventoryAdjustmentHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadInventoryAdjustmentHeadAsync), inner.ReadInventoryAdjustmentHeadAsync, ct);

    public Task<IReadOnlyList<TradeInventoryAdjustmentLine>> ReadInventoryAdjustmentLinesAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadInventoryAdjustmentLinesAsync), inner.ReadInventoryAdjustmentLinesAsync, ct);

    public Task<TradeCustomerReturnHead> ReadCustomerReturnHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadCustomerReturnHeadAsync), inner.ReadCustomerReturnHeadAsync, ct);

    public Task<IReadOnlyList<TradeCustomerReturnLine>> ReadCustomerReturnLinesAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadCustomerReturnLinesAsync), inner.ReadCustomerReturnLinesAsync, ct);

    public Task<TradeVendorReturnHead> ReadVendorReturnHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadVendorReturnHeadAsync), inner.ReadVendorReturnHeadAsync, ct);

    public Task<IReadOnlyList<TradeVendorReturnLine>> ReadVendorReturnLinesAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadVendorReturnLinesAsync), inner.ReadVendorReturnLinesAsync, ct);

    public Task<TradeCustomerPaymentHead> ReadCustomerPaymentHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadCustomerPaymentHeadAsync), inner.ReadCustomerPaymentHeadAsync, ct);

    public Task<TradeVendorPaymentHead> ReadVendorPaymentHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadVendorPaymentHeadAsync), inner.ReadVendorPaymentHeadAsync, ct);

    public Task<TradeItemPriceUpdateHead> ReadItemPriceUpdateHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadItemPriceUpdateHeadAsync), inner.ReadItemPriceUpdateHeadAsync, ct);

    public Task<IReadOnlyList<TradeItemPriceUpdateLine>> ReadItemPriceUpdateLinesAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadItemPriceUpdateLinesAsync), inner.ReadItemPriceUpdateLinesAsync, ct);

    private Task<T> ReadAsync<T>(
        Guid documentId,
        string operation,
        Func<Guid, CancellationToken, Task<T>> reader,
        CancellationToken ct)
        => cache.GetOrAddAsync(
            $"trade:{operation}:{documentId:D}",
            innerCt => reader(documentId, innerCt),
            ct);
}
