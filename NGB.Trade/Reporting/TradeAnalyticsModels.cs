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
