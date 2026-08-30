using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Documents;
using NGB.Core.Reporting.Exceptions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;
using NGB.Trade.Runtime.Policy;

namespace NGB.Trade.Runtime.Reporting;

public sealed class InventoryMovementsCanonicalReportExecutor(
    ITradeAccountingPolicyReader policyReader,
    IOperationalRegisterMovementsQueryReader movementsQueryReader,
    IDocumentRepository documents,
    TimeProvider timeProvider)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => TradeCodes.InventoryMovementsReport;

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var todayUtc = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var rawTo = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "to_utc") ?? todayUtc;
        var rawFrom = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "from_utc")
            ?? new DateOnly(rawTo.Year, rawTo.Month, 1);

        if (rawTo < rawFrom)
        {
            throw Invalid(
                definition,
                "parameters.to_utc",
                $"{CanonicalReportExecutionHelper.GetParameterLabel(definition, "to_utc")} must be on or after {CanonicalReportExecutionHelper.GetParameterLabel(definition, "from_utc")}.");
        }

        var dimensions = TradeReportingHelpers.BuildItemWarehouseFilters(definition, request);
        var policy = await policyReader.GetRequiredAsync(ct);

        var offset = Math.Max(0, request.Offset);
        var requestedLimit = request.Limit <= 0 ? 100 : request.Limit;
        var useLegacyOffset = offset > 0 && string.IsNullOrWhiteSpace(request.Cursor);
        IReadOnlyList<OperationalRegisterMovementQueryReadRow> movementRows;
        int? total;
        bool hasMore;
        string? nextCursor;

        if (useLegacyOffset)
        {
            var legacyPage = await movementsQueryReader.GetByOccurredAtPageAsync(
                policy.InventoryMovementsRegisterId,
                rawFrom,
                rawTo,
                dimensions.Count == 0 ? null : dimensions,
                offset,
                request.DisablePaging ? null : requestedLimit,
                ct);

            if (legacyPage.Total > int.MaxValue)
                throw new InvalidOperationException("Inventory Movements report exceeds the supported row-count range.");

            movementRows = legacyPage.Rows;
            total = (int)legacyPage.Total;
            hasMore = offset + movementRows.Count < total;
            nextCursor = null;
        }
        else
        {
            var cursor = request.DisablePaging || string.IsNullOrWhiteSpace(request.Cursor)
                ? null
                : InventoryMovementCursorCodec.Decode(request.Cursor);
            var rows = await movementsQueryReader.GetByOccurredAtCursorAsync(
                policy.InventoryMovementsRegisterId,
                rawFrom,
                rawTo,
                dimensions.Count == 0 ? null : dimensions,
                cursor,
                checked(requestedLimit + 1),
                ct);

            hasMore = !request.DisablePaging && rows.Count > requestedLimit;
            movementRows = hasMore ? rows.Take(requestedLimit).ToArray() : rows;
            total = request.DisablePaging ? movementRows.Count : null;
            nextCursor = hasMore
                ? InventoryMovementCursorCodec.Encode(new OperationalRegisterOccurredAtCursor(
                    movementRows[^1].OccurredAtUtc,
                    movementRows[^1].MovementId))
                : null;
            offset = 0;
        }

        var limit = request.DisablePaging ? movementRows.Count : requestedLimit;
        var documentMap = await documents.GetByIdsAsync(
            movementRows.Select(static row => row.DocumentId).Distinct().ToArray(),
            ct);

        var renderedRows = movementRows
            .Select(row => ToRow(row, documentMap.GetValueOrDefault(row.DocumentId)))
            .ToArray();

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("date", "Date", "date", Width: 120, IsFrozen: true),
                new ReportSheetColumnDto("item", "Item", "string", Width: 220),
                new ReportSheetColumnDto("warehouse", "Warehouse", "string", Width: 180),
                new ReportSheetColumnDto("document", "Document", "string", Width: 180),
                new ReportSheetColumnDto("qty_in", "Qty In", "decimal", Width: 100),
                new ReportSheetColumnDto("qty_out", "Qty Out", "decimal", Width: 100),
                new ReportSheetColumnDto("qty_delta", "Qty Delta", "decimal", Width: 110),
                new ReportSheetColumnDto("is_storno", "Storno", "bool", Width: 90)
            ],
            Rows: renderedRows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"{rawFrom:yyyy-MM-dd} to {rawTo:yyyy-MM-dd}",
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-trd-inventory-movements"
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: offset,
            limit: limit,
            total: total,
            hasMore: hasMore,
            nextCursor: nextCursor,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-trd-inventory-movements",
                ["from_utc"] = rawFrom.ToString("yyyy-MM-dd"),
                ["to_utc"] = rawTo.ToString("yyyy-MM-dd")
            });
    }

    private static ReportSheetRowDto ToRow(OperationalRegisterMovementQueryReadRow row, DocumentRecord? document)
    {
        var itemDisplay = TradeReportingHelpers.GetDisplay(row.Dimensions, row.DimensionValueDisplays, TradeCodes.Item);
        var warehouseDisplay = TradeReportingHelpers.GetDisplay(row.Dimensions, row.DimensionValueDisplays, TradeCodes.Warehouse);
        var itemId = TradeReportingHelpers.TryGetValueId(row.Dimensions, TradeCodes.Item);
        var warehouseId = TradeReportingHelpers.TryGetValueId(row.Dimensions, TradeCodes.Warehouse);
        var sign = row.IsStorno ? -1m : 1m;
        var documentDisplay = !string.IsNullOrWhiteSpace(document?.Number)
            ? document.Number!
            : row.DocumentId.ToString("D");

        return new ReportSheetRowDto(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(DateOnly.FromDateTime(row.OccurredAtUtc).ToString("yyyy-MM-dd")),
                    DateOnly.FromDateTime(row.OccurredAtUtc).ToString("yyyy-MM-dd"),
                    "date"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(itemDisplay),
                    itemDisplay,
                    "string",
                    Action: itemId is { } actualItemId
                        ? ReportCellActions.BuildCatalogAction(TradeCodes.Item, actualItemId)
                        : null),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(warehouseDisplay),
                    warehouseDisplay,
                    "string",
                    Action: warehouseId is { } actualWarehouseId
                        ? ReportCellActions.BuildCatalogAction(TradeCodes.Warehouse, actualWarehouseId)
                        : null),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(documentDisplay),
                    documentDisplay,
                    "string",
                    Action: document is null
                        ? null
                        : ReportCellActions.BuildDocumentAction(document.TypeCode, document.Id)),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(sign * row.Values.GetValueOrDefault("qty_in")),
                    (sign * row.Values.GetValueOrDefault("qty_in")).ToString("0.####"),
                    "decimal"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(sign * row.Values.GetValueOrDefault("qty_out")),
                    (sign * row.Values.GetValueOrDefault("qty_out")).ToString("0.####"),
                    "decimal"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(sign * row.Values.GetValueOrDefault("qty_delta")),
                    (sign * row.Values.GetValueOrDefault("qty_delta")).ToString("0.####"),
                    "decimal"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(row.IsStorno),
                    row.IsStorno ? "Yes" : "No",
                    "bool")
            ]);
    }

    private static ReportLayoutValidationException Invalid(
        ReportDefinitionDto definition,
        string fieldPath,
        string message)
        => new(
            message,
            fieldPath,
            errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [fieldPath] = [message]
            },
            context: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["reportCode"] = definition.ReportCode
            });
}
