namespace NGB.Trade.Reporting;

public interface ITradeAnalyticsReader
{
    Task<TradeAnalyticsPage<SalesByItemSummaryRow, SalesByItemTotals>> GetSalesByItemPageAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? customerIds,
        IReadOnlyList<Guid>? warehouseIds,
        int offset,
        int limit,
        CancellationToken ct = default);

    Task<IReadOnlyList<SalesByItemSummaryRow>> GetSalesByItemAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? customerIds,
        IReadOnlyList<Guid>? warehouseIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<SalesByCustomerSummaryRow>> GetSalesByCustomerAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<Guid>? customerIds,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? warehouseIds,
        CancellationToken ct = default);

    Task<TradeAnalyticsPage<SalesByCustomerSummaryRow, SalesByCustomerTotals>> GetSalesByCustomerPageAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<Guid>? customerIds,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? warehouseIds,
        int offset,
        int limit,
        CancellationToken ct = default);

    Task<IReadOnlyList<PurchasesByVendorSummaryRow>> GetPurchasesByVendorAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<Guid>? vendorIds,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? warehouseIds,
        CancellationToken ct = default);

    Task<TradeAnalyticsPage<PurchasesByVendorSummaryRow, PurchasesByVendorTotals>> GetPurchasesByVendorPageAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<Guid>? vendorIds,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? warehouseIds,
        int offset,
        int limit,
        CancellationToken ct = default);

    Task<IReadOnlyList<RecentTradeDocumentSummaryRow>> GetRecentDocumentsAsync(
        DateOnly asOf,
        int limit,
        CancellationToken ct = default);
}
