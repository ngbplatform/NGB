using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;
using NGB.Trade.Runtime.Policy;
using NGB.Trade.Reporting;

namespace NGB.Trade.Runtime.Reporting;

public sealed class TradeDashboardOverviewCanonicalReportExecutor(
    ITradeAnalyticsReader analytics,
    ITradeAccountingPolicyReader policyReader,
    ITradeInventoryBalanceReader balanceReader,
    TimeProvider timeProvider)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => TradeCodes.DashboardOverviewReport;

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var asOf = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "as_of_utc")
            ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var fromInclusive = new DateOnly(asOf.Year, asOf.Month, 1);

        var analyticsSnapshot = await analytics.GetDashboardOverviewAsync(
            fromInclusive,
            asOf,
            topItemLimit: 5,
            recentDocumentLimit: 8,
            ct);
        var salesByItem = analyticsSnapshot.SalesByItem;
        var salesByCustomer = analyticsSnapshot.SalesByCustomer;
        var purchasesByVendor = analyticsSnapshot.PurchasesByVendor;
        var recentDocuments = analyticsSnapshot.RecentDocuments;
        var policy = await policyReader.GetRequiredAsync(ct);
        var balances = await balanceReader.GetPageAsync(
            policy.InventoryMovementsRegisterId,
            asOf,
            itemIds: null,
            warehouseIds: null,
            TradeInventoryBalanceSort.AbsoluteQuantityDescending,
            offset: 0,
            limit: 8,
            ct);
        var inventoryPositions = balances.Rows;
        var inventoryPositionCount = balances.Total;

        var salesThisMonth = salesByItem.Totals.NetSales;
        var purchasesThisMonth = analyticsSnapshot.NetPurchases;
        var grossMargin = salesByItem.Totals.GrossMargin;
        var inventoryOnHand = balances.TotalQuantity;
        var topItems = salesByItem.Rows
            .Where(static x => x.NetSales != 0m || x.SoldQuantity != 0m || x.ReturnedQuantity != 0m)
            .Take(5)
            .ToArray();

        var rows = new List<ReportSheetRowDto>
        {
            HeaderRow("Month-to-Date KPIs"),
            MetricRow(
                "Sales This Month",
                salesThisMonth,
                $"{fromInclusive:yyyy-MM-dd} to {asOf:yyyy-MM-dd}",
                "Net invoiced after customer returns.",
                ReportCellActions.BuildReportAction(
                    TradeCodes.SalesByCustomerReport,
                    parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["from_utc"] = fromInclusive.ToString("yyyy-MM-dd"),
                        ["to_utc"] = asOf.ToString("yyyy-MM-dd")
                    })),
            MetricRow(
                "Purchases This Month",
                purchasesThisMonth,
                $"{fromInclusive:yyyy-MM-dd} to {asOf:yyyy-MM-dd}",
                "Net receipts after vendor returns.",
                ReportCellActions.BuildReportAction(
                    TradeCodes.PurchasesByVendorReport,
                    parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["from_utc"] = fromInclusive.ToString("yyyy-MM-dd"),
                        ["to_utc"] = asOf.ToString("yyyy-MM-dd")
                    })),
            MetricRow(
                "Inventory On Hand",
                inventoryOnHand,
                $"As of {asOf:yyyy-MM-dd}",
                "Current quantity across all item and warehouse keys.",
                ReportCellActions.BuildReportAction(
                    TradeCodes.InventoryBalancesReport,
                    parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["as_of_utc"] = asOf.ToString("yyyy-MM-dd")
                    })),
            MetricRow(
                "Gross Margin",
                grossMargin,
                $"{fromInclusive:yyyy-MM-dd} to {asOf:yyyy-MM-dd}",
                "Net sales minus net COGS.",
                ReportCellActions.BuildReportAction(
                    TradeCodes.SalesByItemReport,
                    parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["from_utc"] = fromInclusive.ToString("yyyy-MM-dd"),
                        ["to_utc"] = asOf.ToString("yyyy-MM-dd")
                    })),
            HeaderRow("Top Selling Items")
        };

        if (topItems.Length == 0)
        {
            rows.Add(EmptyRow("No posted sales activity in the selected month."));
        }
        else
        {
            rows.AddRange(topItems.Select(item => TopItemRow(item, fromInclusive, asOf)));
        }

        rows.Add(HeaderRow("Top Customers"));
        if (salesByCustomer is null || salesByCustomer.Rows.Count == 0)
        {
            rows.Add(EmptyRow("No posted customer sales in the selected month."));
        }
        else
        {
            rows.AddRange(salesByCustomer.Rows.Take(5).Select(row => TopCustomerRow(row, fromInclusive, asOf)));
        }

        rows.Add(HeaderRow("Top Vendors"));
        if (purchasesByVendor is null || purchasesByVendor.Rows.Count == 0)
        {
            rows.Add(EmptyRow("No posted vendor purchases in the selected month."));
        }
        else
        {
            rows.AddRange(purchasesByVendor.Rows.Take(5).Select(row => TopVendorRow(row, fromInclusive, asOf)));
        }

        rows.Add(HeaderRow("Largest Inventory Positions"));
        if (inventoryPositions.Count == 0)
        {
            rows.Add(EmptyRow("No inventory balance positions are available yet."));
        }
        else
        {
            rows.AddRange(inventoryPositions.Select(position => InventoryPositionRow(position, asOf)));
        }

        rows.Add(HeaderRow("Recent Documents"));
        if (recentDocuments.Count == 0)
        {
            rows.Add(EmptyRow("No recent trade documents yet."));
        }
        else
        {
            rows.AddRange(recentDocuments.Select(RecentDocumentRow));
        }

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("category", "Category", "string", Width: 150, IsFrozen: true),
                new ReportSheetColumnDto("subject", "Subject", "string", Width: 260),
                new ReportSheetColumnDto("value", "Value", "string", Width: 130),
                new ReportSheetColumnDto("secondary", "Secondary", "string", Width: 150),
                new ReportSheetColumnDto("notes", "Notes", "string", Width: 320)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"As of {asOf:yyyy-MM-dd}",
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-trd-dashboard-overview",
                    ["inventory_position_count"] = inventoryPositionCount.ToString(),
                    ["active_sales_item_count"] = salesByItem.Total.ToString(),
                    ["active_customer_count"] = (salesByCustomer?.Total ?? 0).ToString(),
                    ["active_vendor_count"] = (purchasesByVendor?.Total ?? 0).ToString()
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: 0,
            limit: rows.Count,
            total: rows.Count,
            hasMore: false,
            nextCursor: null,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-trd-dashboard-overview",
                ["as_of_utc"] = asOf.ToString("yyyy-MM-dd"),
                ["inventory_position_count"] = inventoryPositionCount.ToString(),
                ["active_sales_item_count"] = salesByItem.Total.ToString(),
                ["active_customer_count"] = (salesByCustomer?.Total ?? 0).ToString(),
                ["active_vendor_count"] = (purchasesByVendor?.Total ?? 0).ToString()
            });
    }

    private static ReportSheetRowDto HeaderRow(string title)
        => new(
            ReportRowKind.Header,
            Cells:
            [
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(title),
                    title,
                    "string",
                    ColSpan: 5,
                    SemanticRole: "label")
            ],
            SemanticRole: "section_header");

    private static ReportSheetRowDto MetricRow(
        string subject,
        decimal value,
        string secondary,
        string notes,
        ReportCellActionDto action)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("KPI"), "KPI", "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(subject), subject, "string"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(value),
                    value.ToString("0.##"),
                    "decimal",
                    Action: action),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(secondary), secondary, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(notes), notes, "string")
            ]);

    private static ReportSheetRowDto TopItemRow(
        SalesByItemSummaryRow row,
        DateOnly fromInclusive,
        DateOnly asOf)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Top Item"), "Top Item", "string"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.ItemDisplay),
                    row.ItemDisplay,
                    "string",
                    Action: ReportCellActions.BuildCatalogAction(TradeCodes.Item, row.ItemId)),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.NetSales),
                    row.NetSales.ToString("0.##"),
                    "decimal",
                    Action: ReportCellActions.BuildReportAction(
                        TradeCodes.SalesByItemReport,
                        parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["from_utc"] = fromInclusive.ToString("yyyy-MM-dd"),
                            ["to_utc"] = asOf.ToString("yyyy-MM-dd")
                        },
                        filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["item_id"] = new(JsonSerializer.SerializeToElement(row.ItemId))
                        })),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.SoldQuantity - row.ReturnedQuantity),
                    (row.SoldQuantity - row.ReturnedQuantity).ToString("0.##"),
                    "decimal"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue($"Gross Margin {row.GrossMargin:0.##} ({row.MarginPercent:0.##}%)"),
                    $"Gross Margin {row.GrossMargin:0.##} ({row.MarginPercent:0.##}%)",
                    "string")
            ]);

    private static ReportSheetRowDto InventoryPositionRow(TradeInventoryBalanceRow row, DateOnly asOf)
    {
        var quantityAction = ReportCellActions.BuildReportAction(
                TradeCodes.InventoryBalancesReport,
                parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = asOf.ToString("yyyy-MM-dd")
                },
                filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["item_id"] = new(JsonSerializer.SerializeToElement(row.ItemId)),
                    ["warehouse_id"] = new(JsonSerializer.SerializeToElement(row.WarehouseId))
                });

        return new ReportSheetRowDto(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Inventory Position"), "Inventory Position", "string"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.ItemDisplay),
                    row.ItemDisplay,
                    "string",
                    Action: ReportCellActions.BuildCatalogAction(TradeCodes.Item, row.ItemId)),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.Quantity),
                    row.Quantity.ToString("0.####"),
                    "decimal",
                    Action: quantityAction),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.WarehouseDisplay),
                    row.WarehouseDisplay,
                    "string",
                    Action: ReportCellActions.BuildCatalogAction(TradeCodes.Warehouse, row.WarehouseId)),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue("On hand"),
                    "On hand",
                    "string")
            ]);
    }

    private static ReportSheetRowDto TopCustomerRow(
        SalesByCustomerSummaryRow row,
        DateOnly fromInclusive,
        DateOnly asOf)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Top Customer"), "Top Customer", "string"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.CustomerDisplay),
                    row.CustomerDisplay,
                    "string",
                    Action: ReportCellActions.BuildCatalogAction(TradeCodes.Party, row.CustomerId)),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.NetSales),
                    row.NetSales.ToString("0.##"),
                    "decimal",
                    Action: ReportCellActions.BuildReportAction(
                        TradeCodes.SalesByCustomerReport,
                        parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["from_utc"] = fromInclusive.ToString("yyyy-MM-dd"),
                            ["to_utc"] = asOf.ToString("yyyy-MM-dd")
                        },
                        filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["customer_id"] = new(JsonSerializer.SerializeToElement(row.CustomerId))
                        })),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue($"{row.SalesDocumentCount} sales / {row.ReturnDocumentCount} returns"),
                    $"{row.SalesDocumentCount} sales / {row.ReturnDocumentCount} returns",
                    "string"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue($"Gross Margin {row.GrossMargin:0.##} ({row.MarginPercent:0.##}%)"),
                    $"Gross Margin {row.GrossMargin:0.##} ({row.MarginPercent:0.##}%)",
                    "string")
            ]);

    private static ReportSheetRowDto TopVendorRow(
        PurchasesByVendorSummaryRow row,
        DateOnly fromInclusive,
        DateOnly asOf)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Top Vendor"), "Top Vendor", "string"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.VendorDisplay),
                    row.VendorDisplay,
                    "string",
                    Action: ReportCellActions.BuildCatalogAction(TradeCodes.Party, row.VendorId)),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.NetPurchases),
                    row.NetPurchases.ToString("0.##"),
                    "decimal",
                    Action: ReportCellActions.BuildReportAction(
                        TradeCodes.PurchasesByVendorReport,
                        parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["from_utc"] = fromInclusive.ToString("yyyy-MM-dd"),
                            ["to_utc"] = asOf.ToString("yyyy-MM-dd")
                        },
                        filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["vendor_id"] = new(JsonSerializer.SerializeToElement(row.VendorId))
                        })),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue($"{row.PurchaseDocumentCount} purchases / {row.ReturnDocumentCount} returns"),
                    $"{row.PurchaseDocumentCount} purchases / {row.ReturnDocumentCount} returns",
                    "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Net purchases"), "Net purchases", "string")
            ]);

    private static ReportSheetRowDto RecentDocumentRow(RecentTradeDocumentSummaryRow row)
    {
        var notesParts = new List<string> { row.DocumentTypeDisplay, row.StatusDisplay };
        if (!string.IsNullOrWhiteSpace(row.PartnerDisplay))
            notesParts.Add(row.PartnerDisplay!);

        return new ReportSheetRowDto(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Recent Document"), "Recent Document", "string"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.DocumentDisplay),
                    row.DocumentDisplay,
                    "string",
                    Action: ReportCellActions.BuildDocumentAction(row.DocumentTypeCode, row.DocumentId)),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.Amount),
                    row.Amount?.ToString("0.##") ?? string.Empty,
                    row.Amount.HasValue ? "decimal" : "string"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.DocumentDateUtc),
                    row.DocumentDateUtc.ToString("yyyy-MM-dd"),
                    "date"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(string.Join(" · ", notesParts)),
                    string.Join(" · ", notesParts),
                    "string")
            ]);
    }

    private static ReportSheetRowDto EmptyRow(string message)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(message), message, "string", ColSpan: 4)
            ]);
}
