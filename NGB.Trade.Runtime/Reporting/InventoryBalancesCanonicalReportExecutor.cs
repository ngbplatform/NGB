using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;
using NGB.Trade.Runtime.Policy;
using NGB.Trade.Reporting;

namespace NGB.Trade.Runtime.Reporting;

public sealed class InventoryBalancesCanonicalReportExecutor(
    ITradeAccountingPolicyReader policyReader,
    ITradeInventoryBalanceReader balanceReader,
    TimeProvider timeProvider)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => TradeCodes.InventoryBalancesReport;

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var todayUtc = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var asOf = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "as_of_utc") ?? todayUtc;
        var currentMonth = CanonicalReportExecutionHelper.NormalizeToPeriodMonth(asOf);
        var itemIds = CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, request, "item_id");
        var warehouseIds = CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, request, "warehouse_id");
        var policy = await policyReader.GetRequiredAsync(ct);
        var cursorKind = SpecializedReportCursorCodec.BuildKind(
            ReportCode,
            asOf.ToString("yyyy-MM-dd"),
            policy.InventoryMovementsRegisterId.ToString("D"),
            string.Join(',', itemIds.Order()),
            string.Join(',', warehouseIds.Order()));
        var cursor = request.DisablePaging || string.IsNullOrWhiteSpace(request.Cursor)
            ? null
            : SpecializedReportCursorCodec.Decode<TradeInventoryBalancePageCursor>(cursorKind, request.Cursor);
        var offset = cursor?.Offset ?? Math.Max(0, request.Offset);
        var limit = request.DisablePaging
            ? PagingLimits.MaxMaterializedRows + 1
            : request.Limit <= 0 ? 100 : request.Limit;
        var page = cursor is not null || (!request.DisablePaging && offset == 0)
            ? await balanceReader.GetCursorPageAsync(
                policy.InventoryMovementsRegisterId,
                asOf,
                itemIds,
                warehouseIds,
                TradeInventoryBalanceSort.ItemWarehouse,
                cursor,
                limit,
                ct)
            : await balanceReader.GetPageAsync(
                policy.InventoryMovementsRegisterId,
                asOf,
                itemIds,
                warehouseIds,
                TradeInventoryBalanceSort.ItemWarehouse,
                offset,
                limit,
                ct);

        var rows = page.Rows
            .Select(x => ToRow(x, currentMonth, asOf))
            .ToArray();

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("item", "Item", "string", Width: 220, IsFrozen: true),
                new ReportSheetColumnDto("warehouse", "Warehouse", "string", Width: 180),
                new ReportSheetColumnDto("quantity", "Quantity On Hand", "decimal", Width: 140)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"As of {asOf:yyyy-MM-dd}",
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-trd-inventory-balances"
                }));

        var hasMore = page.HasMore || offset + page.Rows.Count < page.Total;
        var nextCursor = !request.DisablePaging && hasMore
            ? SpecializedReportCursorCodec.Encode(
                cursorKind,
                new TradeInventoryBalancePageCursor(
                    offset + page.Rows.Count,
                    page.Total,
                    page.TotalQuantity,
                    page.NextAfterAbsoluteQuantity,
                    page.NextAfterItemDisplay,
                    page.NextAfterWarehouseDisplay,
                    page.NextAfterItemId,
                    page.NextAfterWarehouseId))
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
                ["executor"] = "canonical-trd-inventory-balances",
                ["as_of_utc"] = asOf.ToString("yyyy-MM-dd")
            });
    }

    private static ReportSheetRowDto ToRow(TradeInventoryBalanceRow row, DateOnly monthStart, DateOnly asOf)
    {
        var quantityAction = ReportCellActions.BuildReportAction(
                TradeCodes.InventoryMovementsReport,
                parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = monthStart.ToString("yyyy-MM-dd"),
                    ["to_utc"] = asOf.ToString("yyyy-MM-dd")
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
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.ItemDisplay),
                    row.ItemDisplay,
                    "string",
                    Action: ReportCellActions.BuildCatalogAction(TradeCodes.Item, row.ItemId)),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.WarehouseDisplay),
                    row.WarehouseDisplay,
                    "string",
                    Action: ReportCellActions.BuildCatalogAction(TradeCodes.Warehouse, row.WarehouseId)),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.Quantity),
                    row.Quantity.ToString("0.####"),
                    "decimal",
                    Action: quantityAction)
            ]);
    }
}
