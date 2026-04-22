using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;
using NGB.Trade.Reporting;

namespace NGB.Trade.Runtime.Reporting;

public sealed class PurchasesByVendorCanonicalReportExecutor(
    ITradeAnalyticsReader analytics,
    TimeProvider timeProvider)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => TradeCodes.PurchasesByVendorReport;

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var (fromInclusive, toInclusive) = TradeReportingHelpers.GetDateRangeOrCurrentMonth(definition, request, timeProvider);
        var vendorIds = CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, request, "vendor_id");
        var itemIds = CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, request, "item_id");
        var warehouseIds = CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, request, "warehouse_id");

        var ordered = (await analytics.GetPurchasesByVendorAsync(fromInclusive, toInclusive, vendorIds, itemIds, warehouseIds, ct))
            .Where(static x => x.PurchaseDocumentCount != 0 || x.ReturnDocumentCount != 0)
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
                new ReportSheetColumnDto("vendor", "Vendor", "string", Width: 240, IsFrozen: true),
                new ReportSheetColumnDto("purchase_document_count", "Purchase Docs", "int32", Width: 110),
                new ReportSheetColumnDto("return_document_count", "Return Docs", "int32", Width: 100),
                new ReportSheetColumnDto("gross_purchases", "Gross Purchases", "decimal", Width: 140),
                new ReportSheetColumnDto("returned_amount", "Returned Amount", "decimal", Width: 130),
                new ReportSheetColumnDto("net_purchases", "Net Purchases", "decimal", Width: 130)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"{fromInclusive:yyyy-MM-dd} to {toInclusive:yyyy-MM-dd}",
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-trd-purchases-by-vendor"
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
                ["executor"] = "canonical-trd-purchases-by-vendor",
                ["from_utc"] = fromInclusive.ToString("yyyy-MM-dd"),
                ["to_utc"] = toInclusive.ToString("yyyy-MM-dd")
            });
    }

    private static ReportSheetRowDto ToDetailRow(PurchasesByVendorSummaryRow row)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.VendorDisplay),
                    row.VendorDisplay,
                    "string",
                    Action: ReportCellActions.BuildCatalogAction(TradeCodes.Party, row.VendorId)),
                IntCell(row.PurchaseDocumentCount),
                IntCell(row.ReturnDocumentCount),
                DecimalCell(row.GrossPurchases),
                DecimalCell(row.ReturnedAmount),
                DecimalCell(row.NetPurchases)
            ]);

    private static ReportSheetRowDto ToTotalRow(IReadOnlyList<PurchasesByVendorSummaryRow> rows)
        => new(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Total"), "Total", "string", SemanticRole: "label"),
                IntCell(rows.Sum(static x => x.PurchaseDocumentCount), "total"),
                IntCell(rows.Sum(static x => x.ReturnDocumentCount), "total"),
                DecimalCell(rows.Sum(static x => x.GrossPurchases), "total"),
                DecimalCell(rows.Sum(static x => x.ReturnedAmount), "total"),
                DecimalCell(rows.Sum(static x => x.NetPurchases), "total")
            ],
            SemanticRole: "grand_total");

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
