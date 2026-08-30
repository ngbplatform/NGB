using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
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
        var cursorKind = BuildCursorKind(fromInclusive, toInclusive, itemIds, customerIds, warehouseIds);

        var cursor = request.DisablePaging || string.IsNullOrWhiteSpace(request.Cursor)
            ? null
            : SpecializedReportCursorCodec.Decode<TradeAnalyticsPageCursor<SalesByItemTotals>>(
                cursorKind, request.Cursor);
        var offset = cursor?.Offset ?? Math.Max(0, request.Offset);
        var limit = request.DisablePaging
            ? PagingLimits.MaxMaterializedRows + 1
            : (request.Limit <= 0 ? 100 : request.Limit);
        var page = cursor is not null || (!request.DisablePaging && offset == 0)
            ? await analytics.GetSalesByItemCursorPageAsync(
                fromInclusive, toInclusive, itemIds, customerIds, warehouseIds, cursor, limit, ct)
            : await analytics.GetSalesByItemPageAsync(
                fromInclusive, toInclusive, itemIds, customerIds, warehouseIds, offset, limit, ct);
        var pageRows = page.Rows;

        var rows = pageRows
            .Select(ToDetailRow)
            .ToList();

        if (request.Layout?.ShowGrandTotals != false && page.Total > 0)
            rows.Add(ToTotalRow(page.Totals));

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

        var hasMore = page.HasMore || offset + pageRows.Count < page.Total;
        var nextCursor = !request.DisablePaging && hasMore
            ? SpecializedReportCursorCodec.Encode(
                cursorKind,
                new TradeAnalyticsPageCursor<SalesByItemTotals>(
                    offset + pageRows.Count,
                    page.Total,
                    page.Totals))
            : null;

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: offset,
            limit: limit,
            total: page.Total,
            hasMore: hasMore,
            nextCursor: nextCursor,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-trd-sales-by-item",
                ["from_utc"] = fromInclusive.ToString("yyyy-MM-dd"),
                ["to_utc"] = toInclusive.ToString("yyyy-MM-dd")
            });
    }

    private string BuildCursorKind(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyCollection<Guid> itemIds,
        IReadOnlyCollection<Guid> customerIds,
        IReadOnlyCollection<Guid> warehouseIds)
        => SpecializedReportCursorCodec.BuildKind(
            ReportCode,
            fromInclusive.ToString("yyyy-MM-dd"),
            toInclusive.ToString("yyyy-MM-dd"),
            string.Join(',', itemIds.Order()),
            string.Join(',', customerIds.Order()),
            string.Join(',', warehouseIds.Order()));

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

    private static ReportSheetRowDto ToTotalRow(SalesByItemTotals totals)
    {
        return new ReportSheetRowDto(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Total"), "Total", "string", SemanticRole: "label"),
                DecimalCell(totals.SoldQuantity, semanticRole: "total"),
                DecimalCell(totals.GrossSales, semanticRole: "total"),
                DecimalCell(totals.ReturnedQuantity, semanticRole: "total"),
                DecimalCell(totals.ReturnedAmount, semanticRole: "total"),
                DecimalCell(totals.NetSales, semanticRole: "total"),
                DecimalCell(totals.NetCogs, semanticRole: "total"),
                DecimalCell(totals.GrossMargin, semanticRole: "total"),
                DecimalCell(totals.MarginPercent, semanticRole: "total")
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
