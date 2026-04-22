namespace NGB.Trade.Documents;

public interface ITradeDocumentReaders
{
    Task<TradePurchaseReceiptHead> ReadPurchaseReceiptHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyList<TradePurchaseReceiptLine>> ReadPurchaseReceiptLinesAsync(
        Guid documentId,
        CancellationToken ct = default);

    Task<TradeSalesInvoiceHead> ReadSalesInvoiceHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyList<TradeSalesInvoiceLine>> ReadSalesInvoiceLinesAsync(
        Guid documentId,
        CancellationToken ct = default);

    Task<TradeInventoryTransferHead> ReadInventoryTransferHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyList<TradeInventoryTransferLine>> ReadInventoryTransferLinesAsync(
        Guid documentId,
        CancellationToken ct = default);

    Task<TradeInventoryAdjustmentHead> ReadInventoryAdjustmentHeadAsync(
        Guid documentId,
        CancellationToken ct = default);

    Task<IReadOnlyList<TradeInventoryAdjustmentLine>> ReadInventoryAdjustmentLinesAsync(
        Guid documentId,
        CancellationToken ct = default);

    Task<TradeCustomerReturnHead> ReadCustomerReturnHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyList<TradeCustomerReturnLine>> ReadCustomerReturnLinesAsync(
        Guid documentId,
        CancellationToken ct = default);

    Task<TradeVendorReturnHead> ReadVendorReturnHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyList<TradeVendorReturnLine>> ReadVendorReturnLinesAsync(
        Guid documentId,
        CancellationToken ct = default);

    Task<TradeCustomerPaymentHead> ReadCustomerPaymentHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<TradeVendorPaymentHead> ReadVendorPaymentHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<TradeItemPriceUpdateHead> ReadItemPriceUpdateHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyList<TradeItemPriceUpdateLine>> ReadItemPriceUpdateLinesAsync(
        Guid documentId,
        CancellationToken ct = default);
}
