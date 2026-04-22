using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Persistence.Documents;
using NGB.ReferenceRegisters;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;

namespace NGB.Trade.Runtime.Reporting;

public sealed class CurrentItemPricesCanonicalReportExecutor(
    IReferenceRegisterReadService readService,
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
        var exactFilters = BuildExactFilters(itemIds, priceTypeIds);

        var snapshots = await readService.SliceLastAllEnrichedAsync(
            registerId: ReferenceRegisterId.FromCode(TradeCodes.ItemPricesRegisterCode),
            asOfUtc: DateTime.UtcNow,
            requiredDimensions: exactFilters,
            includeDeleted: false,
            ct: ct);

        var itemFilter = itemIds.Count == 0 ? null : itemIds.ToHashSet();
        var priceTypeFilter = priceTypeIds.Count == 0 ? null : priceTypeIds.ToHashSet();

        var ordered = snapshots
            .Where(snapshot => MatchesFilters(snapshot, itemFilter, priceTypeFilter))
            .OrderBy(static x => GetDisplay(x, TradeCodes.Item))
            .ThenBy(static x => GetDisplay(x, TradeCodes.PriceType))
            .ThenBy(static x => GetCurrency(x), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var offset = Math.Max(0, request.Offset);
        var limit = request.DisablePaging ? ordered.Length : (request.Limit <= 0 ? 100 : request.Limit);
        var pageRows = ordered.Skip(offset).Take(limit).ToArray();
        var sourceDocumentRefs = await ResolveDocumentRefsAsync(pageRows, ct);

        var rows = pageRows
            .Select(snapshot => ToRow(snapshot, sourceDocumentRefs))
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
                Subtitle: $"Active keys: {ordered.Length}",
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-trd-current-item-prices"
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
                ["executor"] = "canonical-trd-current-item-prices"
            });
    }

    private static IReadOnlyList<DimensionValue>? BuildExactFilters(
        IReadOnlyList<Guid> itemIds,
        IReadOnlyList<Guid> priceTypeIds)
    {
        var filters = new List<DimensionValue>(capacity: 2);

        if (itemIds.Count == 1)
        {
            filters.Add(new DimensionValue(
                DeterministicGuid.Create($"Dimension|{TradeCodes.Item}"),
                itemIds[0]));
        }

        if (priceTypeIds.Count == 1)
        {
            filters.Add(new DimensionValue(
                DeterministicGuid.Create($"Dimension|{TradeCodes.PriceType}"),
                priceTypeIds[0]));
        }

        return filters.Count == 0 ? null : filters;
    }

    private static bool MatchesFilters(
        ReferenceRegisterRecordSnapshot snapshot,
        IReadOnlySet<Guid>? itemIds,
        IReadOnlySet<Guid>? priceTypeIds)
    {
        var itemId = GetDimensionValueId(snapshot, TradeCodes.Item);
        if (itemIds is not null && (!itemId.HasValue || !itemIds.Contains(itemId.Value)))
            return false;

        var priceTypeId = GetDimensionValueId(snapshot, TradeCodes.PriceType);
        return priceTypeIds is null || (priceTypeId.HasValue && priceTypeIds.Contains(priceTypeId.Value));
    }

    private async Task<IReadOnlyDictionary<Guid, DocumentDisplayRef>> ResolveDocumentRefsAsync(
        IReadOnlyList<ReferenceRegisterRecordSnapshot> snapshots,
        CancellationToken ct)
    {
        var ids = snapshots
            .Select(GetSourceDocumentId)
            .Where(static id => id.HasValue)
            .Select(static id => id!.Value)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
            return new Dictionary<Guid, DocumentDisplayRef>();

        return await documentDisplayReader.ResolveRefsAsync(ids, ct);
    }

    private static ReportSheetRowDto ToRow(
        ReferenceRegisterRecordSnapshot snapshot,
        IReadOnlyDictionary<Guid, DocumentDisplayRef> sourceDocumentRefs)
    {
        var itemDisplay = GetDisplay(snapshot, TradeCodes.Item);
        var priceTypeDisplay = GetDisplay(snapshot, TradeCodes.PriceType);
        var itemId = GetDimensionValueId(snapshot, TradeCodes.Item);
        var priceTypeId = GetDimensionValueId(snapshot, TradeCodes.PriceType);
        var currency = GetCurrency(snapshot);
        var unitPrice = Convert.ToDecimal(snapshot.Record.Values.GetValueOrDefault("unit_price") ?? 0m);
        var effectiveDate = TryFormatDate(snapshot.Record.Values.GetValueOrDefault("effective_date"));
        var sourceDocumentId = GetSourceDocumentId(snapshot);
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
                    CanonicalReportExecutionHelper.JsonValue(itemDisplay),
                    itemDisplay,
                    "string",
                    Action: itemId is { } actualItemId
                        ? ReportCellActions.BuildCatalogAction(TradeCodes.Item, actualItemId)
                        : null),
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(priceTypeDisplay),
                    priceTypeDisplay,
                    "string",
                    Action: priceTypeId is { } actualPriceTypeId
                        ? ReportCellActions.BuildCatalogAction(TradeCodes.PriceType, actualPriceTypeId)
                        : null),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(currency), currency, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(unitPrice), unitPrice.ToString("0.####"), "decimal"),
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

    private static Guid? GetSourceDocumentId(ReferenceRegisterRecordSnapshot snapshot)
        => TryGetGuid(snapshot.Record.Values.GetValueOrDefault("source_document_id"))
           ?? snapshot.Record.RecorderDocumentId;

    private static string GetDisplay(ReferenceRegisterRecordSnapshot snapshot, string dimensionCode)
    {
        var dimensionId = DeterministicGuid.Create($"Dimension|{dimensionCode}");
        var value = snapshot.Dimensions.Items.FirstOrDefault(x => x.DimensionId == dimensionId);
        if (value.ValueId == Guid.Empty)
            return string.Empty;

        return snapshot.DimensionValueDisplaysByDimensionId.TryGetValue(dimensionId, out var display)
            ? display
            : value.ValueId.ToString("D");
    }

    private static Guid? GetDimensionValueId(ReferenceRegisterRecordSnapshot snapshot, string dimensionCode)
    {
        var dimensionId = DeterministicGuid.Create($"Dimension|{dimensionCode}");
        var value = snapshot.Dimensions.Items.FirstOrDefault(x => x.DimensionId == dimensionId);
        return value.ValueId == Guid.Empty ? null : value.ValueId;
    }

    private static string GetCurrency(ReferenceRegisterRecordSnapshot snapshot)
        => Convert.ToString(snapshot.Record.Values.GetValueOrDefault("currency")) ?? string.Empty;

    private static string? TryFormatDate(object? value)
    {
        return value switch
        {
            null => null,
            DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd"),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd"),
            DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime.ToString("yyyy-MM-dd"),
            string text when DateOnly.TryParse(text, out var parsedDateOnly) => parsedDateOnly.ToString("yyyy-MM-dd"),
            string text when DateTime.TryParse(text, out var parsedDateTime) => parsedDateTime.ToString("yyyy-MM-dd"),
            _ => null
        };
    }

    private static Guid? TryGetGuid(object? value)
    {
        return value switch
        {
            null => null,
            Guid guid when guid != Guid.Empty => guid,
            string text when Guid.TryParse(text, out var parsed) && parsed != Guid.Empty => parsed,
            _ => null
        };
    }
}
