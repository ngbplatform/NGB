using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;
using NGB.Trade.Reporting;

namespace NGB.Trade.Runtime.Reporting;

public sealed class SalesByItemCanonicalReportExecutor(
    ITradeAnalyticsReader analytics,
    TimeProvider timeProvider)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => TradeCodes.SalesByItemReport;

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var (fromInclusive, toInclusive) = TradeReportingHelpers.GetDateRangeOrCurrentMonth(definition, request, timeProvider);
        var itemIds = CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, request, "item_id");
        var customerIds = CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, request, "customer_id");
        var warehouseIds = CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, request, "warehouse_id");

        var ordered = (await analytics.GetSalesByItemAsync(fromInclusive, toInclusive, itemIds, customerIds, warehouseIds, ct))
            .Where(static x => x.SoldQuantity != 0m || x.ReturnedQuantity != 0m)
            .ToArray();

        var offset = Math.Max(0, request.Offset);
        var limit = request.DisablePaging
            ? ordered.Length
            : (request.Limit <= 0 ? 100 : request.Limit);
        var pageRows = ordered.Skip(offset).Take(limit).ToArray();

        var rows = pageRows
            .Select(ToDetailRow)
            .ToList();

        if (request.Layout?.ShowGrandTotals != false && ordered.Length > 0)
            rows.Add(ToTotalRow(ordered));

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("item", "Item", "string", Width: 240, IsFrozen: true),
                new ReportSheetColumnDto("sold_quantity", "Qty Sold", "decimal", Width: 110),
                new ReportSheetColumnDto("gross_sales", "Gross Sales", "decimal", Width: 120),
                new ReportSheetColumnDto("returned_quantity", "Qty Returned", "decimal", Width: 120),
                new ReportSheetColumnDto("returned_amount", "Returned Amount", "decimal", Width: 130),
                new ReportSheetColumnDto("net_sales", "Net Sales", "decimal", Width: 120),
                new ReportSheetColumnDto("net_cogs", "Net COGS", "decimal", Width: 120),
                new ReportSheetColumnDto("gross_margin", "Gross Margin", "decimal", Width: 130),
                new ReportSheetColumnDto("margin_percent", "Margin %", "decimal", Width: 100)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"{fromInclusive:yyyy-MM-dd} to {toInclusive:yyyy-MM-dd}",
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-trd-sales-by-item"
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: offset,
            limit: limit,
            total: ordered.Length,
            hasMore: offset + pageRows.Length < ordered.Length,
            nextCursor: null,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-trd-sales-by-item",
                ["from_utc"] = fromInclusive.ToString("yyyy-MM-dd"),
                ["to_utc"] = toInclusive.ToString("yyyy-MM-dd")
            });
    }

    private static ReportSheetRowDto ToDetailRow(SalesByItemSummaryRow row)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.ItemDisplay),
                    row.ItemDisplay,
                    "string",
                    Action: ReportCellActions.BuildCatalogAction(TradeCodes.Item, row.ItemId)),
                DecimalCell(row.SoldQuantity),
                DecimalCell(row.GrossSales),
                DecimalCell(row.ReturnedQuantity),
                DecimalCell(row.ReturnedAmount),
                DecimalCell(row.NetSales),
                DecimalCell(row.NetCogs),
                DecimalCell(row.GrossMargin),
                DecimalCell(row.MarginPercent)
            ]);

    private static ReportSheetRowDto ToTotalRow(IReadOnlyList<SalesByItemSummaryRow> rows)
    {
        var soldQuantity = rows.Sum(static x => x.SoldQuantity);
        var grossSales = rows.Sum(static x => x.GrossSales);
        var returnedQuantity = rows.Sum(static x => x.ReturnedQuantity);
        var returnedAmount = rows.Sum(static x => x.ReturnedAmount);
        var netSales = rows.Sum(static x => x.NetSales);
        var netCogs = rows.Sum(static x => x.NetCogs);
        var grossMargin = rows.Sum(static x => x.GrossMargin);
        var marginPercent = netSales == 0m
            ? 0m
            : Math.Round((grossMargin / netSales) * 100m, 2, MidpointRounding.AwayFromZero);

        return new ReportSheetRowDto(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Total"), "Total", "string", SemanticRole: "label"),
                DecimalCell(soldQuantity, semanticRole: "total"),
                DecimalCell(grossSales, semanticRole: "total"),
                DecimalCell(returnedQuantity, semanticRole: "total"),
                DecimalCell(returnedAmount, semanticRole: "total"),
                DecimalCell(netSales, semanticRole: "total"),
                DecimalCell(netCogs, semanticRole: "total"),
                DecimalCell(grossMargin, semanticRole: "total"),
                DecimalCell(marginPercent, semanticRole: "total")
            ],
            SemanticRole: "grand_total");
    }

    private static ReportCellDto DecimalCell(decimal value, string? semanticRole = null)
        => new(
            CanonicalReportExecutionHelper.JsonValue(value),
            value.ToString("0.##"),
            "decimal",
            SemanticRole: semanticRole);
}
