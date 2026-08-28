using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;
using NGB.Trade.Reporting;

namespace NGB.Trade.Runtime.Reporting;

public sealed class SalesByCustomerCanonicalReportExecutor(
    ITradeAnalyticsReader analytics,
    TimeProvider timeProvider)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => TradeCodes.SalesByCustomerReport;

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var (fromInclusive, toInclusive) = TradeReportingHelpers.GetDateRangeOrCurrentMonth(definition, request, timeProvider);
        var customerIds = CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, request, "customer_id");
        var itemIds = CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, request, "item_id");
        var warehouseIds = CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, request, "warehouse_id");

        var offset = Math.Max(0, request.Offset);
        var limit = request.DisablePaging
            ? PagingLimits.MaxMaterializedRows + 1
            : (request.Limit <= 0 ? 100 : request.Limit);
        var page = await analytics.GetSalesByCustomerPageAsync(
            fromInclusive, toInclusive, customerIds, itemIds, warehouseIds, offset, limit, ct);
        var pageRows = page.Rows;

        var rows = pageRows
            .Select(row => ToDetailRow(row, fromInclusive, toInclusive))
            .ToList();

        if (request.Layout?.ShowGrandTotals != false && page.Total > 0)
            rows.Add(ToTotalRow(page.Totals));

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("customer", "Customer", "string", Width: 240, IsFrozen: true),
                new ReportSheetColumnDto("sales_document_count", "Sales Docs", "int32", Width: 100),
                new ReportSheetColumnDto("return_document_count", "Return Docs", "int32", Width: 100),
                new ReportSheetColumnDto("gross_sales", "Gross Sales", "decimal", Width: 120),
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
                    ["executor"] = "canonical-trd-sales-by-customer"
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: offset,
            limit: limit,
            total: page.Total,
            hasMore: offset + pageRows.Count < page.Total,
            nextCursor: null,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-trd-sales-by-customer",
                ["from_utc"] = fromInclusive.ToString("yyyy-MM-dd"),
                ["to_utc"] = toInclusive.ToString("yyyy-MM-dd")
            });
    }

    private static ReportSheetRowDto ToDetailRow(
        SalesByCustomerSummaryRow row,
        DateOnly fromInclusive,
        DateOnly toInclusive)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.CustomerDisplay),
                    row.CustomerDisplay,
                    "string",
                    Action: ReportCellActions.BuildCatalogAction(TradeCodes.Party, row.CustomerId)),
                IntCell(row.SalesDocumentCount),
                IntCell(row.ReturnDocumentCount),
                DecimalCell(row.GrossSales),
                DecimalCell(row.ReturnedAmount),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.NetSales),
                    row.NetSales.ToString("0.##"),
                    "decimal",
                    Action: ReportCellActions.BuildReportAction(
                        TradeCodes.SalesByItemReport,
                        parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["from_utc"] = fromInclusive.ToString("yyyy-MM-dd"),
                            ["to_utc"] = toInclusive.ToString("yyyy-MM-dd")
                        },
                        filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["customer_id"] = new(JsonSerializer.SerializeToElement(row.CustomerId))
                        })),
                DecimalCell(row.NetCogs),
                DecimalCell(row.GrossMargin),
                DecimalCell(row.MarginPercent)
            ]);

    private static ReportSheetRowDto ToTotalRow(SalesByCustomerTotals totals)
    {
        return new ReportSheetRowDto(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Total"), "Total", "string", SemanticRole: "label"),
                IntCell(totals.SalesDocumentCount, "total"),
                IntCell(totals.ReturnDocumentCount, "total"),
                DecimalCell(totals.GrossSales, "total"),
                DecimalCell(totals.ReturnedAmount, "total"),
                DecimalCell(totals.NetSales, "total"),
                DecimalCell(totals.NetCogs, "total"),
                DecimalCell(totals.GrossMargin, "total"),
                DecimalCell(totals.MarginPercent, "total")
            ],
            SemanticRole: "grand_total");
    }

    private static ReportCellDto DecimalCell(decimal value, string? semanticRole = null)
        => new(
            CanonicalReportExecutionHelper.JsonValue(value),
            value.ToString("0.##"),
            "decimal",
            SemanticRole: semanticRole);

    private static ReportCellDto IntCell(int value, string? semanticRole = null)
        => new(
            CanonicalReportExecutionHelper.JsonValue(value),
            value.ToString(),
            "int32",
            SemanticRole: semanticRole);
}
