namespace NGB.Trade.Reporting;

public interface ITradeAnalyticsReader
{
    async Task<TradeDashboardAnalyticsSnapshot> GetDashboardOverviewAsync(
        DateOnly fromInclusive,
        DateOnly asOfInclusive,
        int topItemLimit,
        int recentDocumentLimit,
        CancellationToken ct = default)
    {
        var sales = await GetSalesByItemPageAsync(
            fromInclusive, asOfInclusive, null, null, null, 0, topItemLimit, ct);
        var purchases = await GetPurchasesByVendorPageAsync(
            fromInclusive, asOfInclusive, null, null, null, 0, 1, ct);
        var recent = await GetRecentDocumentsAsync(asOfInclusive, recentDocumentLimit, ct);

        return new TradeDashboardAnalyticsSnapshot(sales, purchases.Totals.NetPurchases, recent);
    }

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
