using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;
using NGB.Trade.Reporting;

namespace NGB.Trade.Runtime.Reporting;

public sealed class CurrentItemPricesCanonicalReportExecutor(
    ITradeCurrentItemPriceReader priceReader,
    IDocumentDisplayReader documentDisplayReader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => TradeCodes.CurrentItemPricesReport;

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var itemIds = CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, request, "item_id");
        var priceTypeIds = CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, request, "price_type_id");
        var offset = Math.Max(0, request.Offset);
        var limit = request.DisablePaging
            ? PagingLimits.MaxMaterializedRows + 1
            : request.Limit <= 0 ? 100 : request.Limit;
        var page = await priceReader.GetPageAsync(
            DateTime.UtcNow,
            itemIds,
            priceTypeIds,
            offset,
            limit,
            ct);
        var sourceDocumentRefs = await ResolveDocumentRefsAsync(page.Rows, ct);

        var rows = page.Rows
            .Select(row => ToRow(row, sourceDocumentRefs))
            .ToArray();

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("item", "Item", "string", Width: 220, IsFrozen: true),
                new ReportSheetColumnDto("price_type", "Price Type", "string", Width: 180),
                new ReportSheetColumnDto("currency", "Currency", "string", Width: 110),
                new ReportSheetColumnDto("unit_price", "Unit Price", "decimal", Width: 120),
                new ReportSheetColumnDto("effective_date", "Effective Date", "date", Width: 120),
                new ReportSheetColumnDto("source_document", "Source Document", "string", Width: 180)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"Active keys: {page.Total}",
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-trd-current-item-prices"
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: offset,
            limit: limit,
            total: page.Total,
            hasMore: offset + page.Rows.Count < page.Total,
            nextCursor: null,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-trd-current-item-prices"
            });
    }

    private async Task<IReadOnlyDictionary<Guid, DocumentDisplayRef>> ResolveDocumentRefsAsync(
        IReadOnlyList<TradeCurrentItemPriceRow> rows,
        CancellationToken ct)
    {
        var ids = rows
            .Select(static row => row.SourceDocumentId)
            .Where(static id => id.HasValue)
            .Select(static id => id!.Value)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
            return new Dictionary<Guid, DocumentDisplayRef>();

        return await documentDisplayReader.ResolveRefsAsync(ids, ct);
    }

    private static ReportSheetRowDto ToRow(
        TradeCurrentItemPriceRow row,
        IReadOnlyDictionary<Guid, DocumentDisplayRef> sourceDocumentRefs)
    {
        var effectiveDate = row.EffectiveDate?.ToString("yyyy-MM-dd");
        var sourceDocumentId = row.SourceDocumentId;
        var sourceDocumentRef = sourceDocumentId is { } actualSourceDocumentId
            && sourceDocumentRefs.TryGetValue(actualSourceDocumentId, out var resolvedRef)
                ? resolvedRef
                : null;
        var sourceDocumentDisplay = sourceDocumentRef?.Display
            ?? sourceDocumentId?.ToString("D");
        var sourceDocumentType = sourceDocumentRef?.TypeCode ?? TradeCodes.ItemPriceUpdate;

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
                    CanonicalReportExecutionHelper.JsonValue(row.PriceTypeDisplay),
                    row.PriceTypeDisplay,
                    "string",
                    Action: ReportCellActions.BuildCatalogAction(TradeCodes.PriceType, row.PriceTypeId)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.Currency), row.Currency, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.UnitPrice), row.UnitPrice.ToString("0.####"), "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(effectiveDate), effectiveDate, "date"),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(sourceDocumentDisplay),
                    sourceDocumentDisplay,
                    "string",
                    Action: sourceDocumentId.HasValue
                        ? ReportCellActions.BuildDocumentAction(sourceDocumentType, sourceDocumentId.Value)
                        : null)
            ]);
    }
}
