using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;
using NGB.Trade.Runtime.Policy;
using NGB.Trade.Reporting;

namespace NGB.Trade.Runtime.Reporting;

public sealed class TradeDashboardOverviewCanonicalReportExecutor(
    ITradeAnalyticsReader analytics,
    ITradeAccountingPolicyReader policyReader,
    IOperationalRegisterReadService readService,
    IOperationalRegisterMovementsQueryReader movementsQueryReader,
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

        var salesByItem = await analytics.GetSalesByItemAsync(
            fromInclusive,
            asOf,
            itemIds: null,
            customerIds: null,
            warehouseIds: null,
            ct);

        var purchasesByVendor = await analytics.GetPurchasesByVendorAsync(
            fromInclusive,
            asOf,
            vendorIds: null,
            itemIds: null,
            warehouseIds: null,
            ct);

        var recentDocuments = await analytics.GetRecentDocumentsAsync(asOf, limit: 8, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var balances = await TradeReportingHelpers.ReadInventoryBalancesAsync(
            readService,
            movementsQueryReader,
            policy.InventoryMovementsRegisterId,
            asOf,
            dimensions: null,
            ct);
        var inventoryPositions = balances
            .Where(static x => x.Quantity != 0m)
            .OrderByDescending(static x => Math.Abs(x.Quantity))
            .ThenBy(static x => TradeReportingHelpers.GetDisplay(x.Bag, x.Displays, TradeCodes.Item), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => TradeReportingHelpers.GetDisplay(x.Bag, x.Displays, TradeCodes.Warehouse), StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        var inventoryPositionCount = balances.Count(static x => x.Quantity != 0m);

        var salesThisMonth = salesByItem.Sum(static x => x.NetSales);
        var purchasesThisMonth = purchasesByVendor.Sum(static x => x.NetPurchases);
        var grossMargin = salesByItem.Sum(static x => x.GrossMargin);
        var inventoryOnHand = balances.Sum(static x => x.Quantity);
        var topItems = salesByItem
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

        rows.Add(HeaderRow("Largest Inventory Positions"));
        if (inventoryPositions.Length == 0)
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
                    ["inventory_position_count"] = inventoryPositionCount.ToString()
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
                ["inventory_position_count"] = inventoryPositionCount.ToString()
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

    private static ReportSheetRowDto InventoryPositionRow(InventoryBalanceSnapshot row, DateOnly asOf)
    {
        var itemDisplay = TradeReportingHelpers.GetDisplay(row.Bag, row.Displays, TradeCodes.Item);
        var warehouseDisplay = TradeReportingHelpers.GetDisplay(row.Bag, row.Displays, TradeCodes.Warehouse);
        var itemId = TradeReportingHelpers.TryGetValueId(row.Bag, TradeCodes.Item);
        var warehouseId = TradeReportingHelpers.TryGetValueId(row.Bag, TradeCodes.Warehouse);

        ReportCellActionDto? quantityAction = null;
        if (itemId is { } actualItemId && warehouseId is { } actualWarehouseId)
        {
            quantityAction = ReportCellActions.BuildReportAction(
                TradeCodes.InventoryBalancesReport,
                parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = asOf.ToString("yyyy-MM-dd")
                },
                filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["item_id"] = new(JsonSerializer.SerializeToElement(actualItemId)),
                    ["warehouse_id"] = new(JsonSerializer.SerializeToElement(actualWarehouseId))
                });
        }

        return new ReportSheetRowDto(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Inventory Position"), "Inventory Position", "string"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(itemDisplay),
                    itemDisplay,
                    "string",
                    Action: itemId is { } catalogItemId
                        ? ReportCellActions.BuildCatalogAction(TradeCodes.Item, catalogItemId)
                        : null),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.Quantity),
                    row.Quantity.ToString("0.####"),
                    "decimal",
                    Action: quantityAction),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(warehouseDisplay),
                    warehouseDisplay,
                    "string",
                    Action: warehouseId is { } catalogWarehouseId
                        ? ReportCellActions.BuildCatalogAction(TradeCodes.Warehouse, catalogWarehouseId)
                        : null),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue("On hand"),
                    "On hand",
                    "string")
            ]);
    }

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
