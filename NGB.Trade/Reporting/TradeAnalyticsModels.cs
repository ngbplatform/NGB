namespace NGB.Trade.Reporting;

public sealed record SalesByItemSummaryRow(
    Guid ItemId,
    string ItemDisplay,
    decimal SoldQuantity,
    decimal GrossSales,
    decimal ReturnedQuantity,
    decimal ReturnedAmount,
    decimal NetSales,
    decimal NetCogs)
{
    public decimal GrossMargin => NetSales - NetCogs;
    public decimal MarginPercent => NetSales == 0m
        ? 0m
        : Math.Round((GrossMargin / NetSales) * 100m, 2, MidpointRounding.AwayFromZero);
}

public sealed record SalesByCustomerSummaryRow(
    Guid CustomerId,
    string CustomerDisplay,
    int SalesDocumentCount,
    int ReturnDocumentCount,
    decimal GrossSales,
    decimal ReturnedAmount,
    decimal NetSales,
    decimal NetCogs)
{
    public decimal GrossMargin => NetSales - NetCogs;
    public decimal MarginPercent => NetSales == 0m
        ? 0m
        : Math.Round((GrossMargin / NetSales) * 100m, 2, MidpointRounding.AwayFromZero);
}

public sealed record PurchasesByVendorSummaryRow(
    Guid VendorId,
    string VendorDisplay,
    int PurchaseDocumentCount,
    int ReturnDocumentCount,
    decimal GrossPurchases,
    decimal ReturnedAmount,
    decimal NetPurchases);

public sealed record TradeAnalyticsPage<TRow, TTotals>(
    IReadOnlyList<TRow> Rows,
    int Total,
    TTotals Totals,
    bool HasMore = false,
    decimal? NextAfterAmount = null,
    string? NextAfterDisplay = null,
    Guid? NextAfterId = null);

public sealed record TradeAnalyticsPageCursor<TTotals>(
    int Offset,
    int Total,
    TTotals Totals,
    decimal? AfterAmount = null,
    string? AfterDisplay = null,
    Guid? AfterId = null);

public sealed record TradeDashboardAnalyticsSnapshot(
    TradeAnalyticsPage<SalesByItemSummaryRow, SalesByItemTotals> SalesByItem,
    decimal NetPurchases,
    IReadOnlyList<RecentTradeDocumentSummaryRow> RecentDocuments);

public sealed record SalesByItemTotals(
    decimal SoldQuantity,
    decimal GrossSales,
    decimal ReturnedQuantity,
    decimal ReturnedAmount,
    decimal NetSales,
    decimal NetCogs)
{
    public decimal GrossMargin => NetSales - NetCogs;
    public decimal MarginPercent => NetSales == 0m
        ? 0m
        : Math.Round((GrossMargin / NetSales) * 100m, 2, MidpointRounding.AwayFromZero);
}

public sealed record SalesByCustomerTotals(
    int SalesDocumentCount,
    int ReturnDocumentCount,
    decimal GrossSales,
    decimal ReturnedAmount,
    decimal NetSales,
    decimal NetCogs)
{
    public decimal GrossMargin => NetSales - NetCogs;
    public decimal MarginPercent => NetSales == 0m
        ? 0m
        : Math.Round((GrossMargin / NetSales) * 100m, 2, MidpointRounding.AwayFromZero);
}

public sealed record PurchasesByVendorTotals(
    int PurchaseDocumentCount,
    int ReturnDocumentCount,
    decimal GrossPurchases,
    decimal ReturnedAmount,
    decimal NetPurchases);

public sealed record RecentTradeDocumentSummaryRow(
    Guid DocumentId,
    string DocumentTypeCode,
    string DocumentTypeDisplay,
    string DocumentDisplay,
    DateOnly DocumentDateUtc,
    DateTime UpdatedAtUtc,
    string StatusDisplay,
    string? PartnerDisplay,
    decimal? Amount);
