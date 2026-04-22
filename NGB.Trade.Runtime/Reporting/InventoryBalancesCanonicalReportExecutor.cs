using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;
using NGB.Trade.Runtime.Policy;

namespace NGB.Trade.Runtime.Reporting;

public sealed class InventoryBalancesCanonicalReportExecutor(
    ITradeAccountingPolicyReader policyReader,
    IOperationalRegisterReadService readService,
    IOperationalRegisterMovementsQueryReader movementsQueryReader,
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
        var dimensions = TradeReportingHelpers.BuildItemWarehouseFilters(definition, request);
        var policy = await policyReader.GetRequiredAsync(ct);
        var balances = await TradeReportingHelpers.ReadInventoryBalancesAsync(
            readService,
            movementsQueryReader,
            policy.InventoryMovementsRegisterId,
            asOf,
            dimensions,
            ct);

        var ordered = balances
            .Where(static x => x.Quantity != 0m)
            .OrderBy(static x => TradeReportingHelpers.GetDisplay(x.Bag, x.Displays, TradeCodes.Item), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => TradeReportingHelpers.GetDisplay(x.Bag, x.Displays, TradeCodes.Warehouse), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var offset = Math.Max(0, request.Offset);
        var limit = request.DisablePaging ? ordered.Length : (request.Limit <= 0 ? 100 : request.Limit);
        var pageRows = ordered.Skip(offset).Take(limit).ToArray();

        var rows = pageRows
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

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: offset,
            limit: limit,
            total: ordered.Length,
            hasMore: offset + pageRows.Length < ordered.Length,
            nextCursor: null,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-trd-inventory-balances",
                ["as_of_utc"] = asOf.ToString("yyyy-MM-dd")
            });
    }

    private static ReportSheetRowDto ToRow(InventoryBalanceSnapshot row, DateOnly monthStart, DateOnly asOf)
    {
        var itemDisplay = TradeReportingHelpers.GetDisplay(row.Bag, row.Displays, TradeCodes.Item);
        var warehouseDisplay = TradeReportingHelpers.GetDisplay(row.Bag, row.Displays, TradeCodes.Warehouse);
        var itemId = TradeReportingHelpers.TryGetValueId(row.Bag, TradeCodes.Item);
        var warehouseId = TradeReportingHelpers.TryGetValueId(row.Bag, TradeCodes.Warehouse);

        ReportCellActionDto? quantityAction = null;
        if (itemId is { } actualItemId && warehouseId is { } actualWarehouseId)
        {
            quantityAction = ReportCellActions.BuildReportAction(
                TradeCodes.InventoryMovementsReport,
                parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = monthStart.ToString("yyyy-MM-dd"),
                    ["to_utc"] = asOf.ToString("yyyy-MM-dd")
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
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(itemDisplay),
                    itemDisplay,
                    "string",
                    Action: itemId is { } catalogItemId
                        ? ReportCellActions.BuildCatalogAction(TradeCodes.Item, catalogItemId)
                        : null),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(warehouseDisplay),
                    warehouseDisplay,
                    "string",
                    Action: warehouseId is { } catalogWarehouseId
                        ? ReportCellActions.BuildCatalogAction(TradeCodes.Warehouse, catalogWarehouseId)
                        : null),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.Quantity),
                    row.Quantity.ToString("0.####"),
                    "decimal",
                    Action: quantityAction)
            ]);
    }
}
