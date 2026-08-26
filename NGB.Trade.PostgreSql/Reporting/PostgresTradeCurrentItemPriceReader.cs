using Dapper;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Trade.Reporting;

namespace NGB.Trade.PostgreSql.Reporting;

public sealed class PostgresTradeCurrentItemPriceReader(IUnitOfWork uow, IReferenceRegisterRepository registers)
    : ITradeCurrentItemPriceReader
{
    private static readonly Guid ItemDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Item}");
    private static readonly Guid PriceTypeDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.PriceType}");

    public async Task<TradeCurrentItemPricePage> GetPageAsync(
        DateTime asOfUtc,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? priceTypeIds,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        asOfUtc.EnsureUtc(nameof(asOfUtc));

        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Offset must be zero or greater.");

        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than zero.");

        await uow.EnsureConnectionOpenAsync(ct);

        var register = await registers.GetByCodeAsync(TradeCodes.ItemPricesRegisterCode, ct)
            ?? throw new NgbConfigurationViolationException($"Reference register '{TradeCodes.ItemPricesRegisterCode}' is not configured.");
        
        var tableName = ReferenceRegisterNaming.RecordsTable(register.TableCode);
        var exists = await uow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT to_regclass(@TableName) IS NOT NULL;",
            new { TableName = tableName },
            uow.Transaction,
            cancellationToken: ct));

        if (!exists)
            return new TradeCurrentItemPricePage([], 0);

        var itemIdArray = NormalizeIds(itemIds);
        var priceTypeIdArray = NormalizeIds(priceTypeIds);
        var sql = $"""
WITH latest AS (
    SELECT DISTINCT ON (record.dimension_set_id)
        record.dimension_set_id,
        record.currency,
        record.unit_price,
        record.effective_date,
        record.source_document_id,
        record.is_deleted
    FROM {tableName} record
    WHERE record.recorded_at_utc <= @AsOfUtc
    ORDER BY record.dimension_set_id, record.recorded_at_utc DESC, record.record_id DESC
),
enriched AS (
    SELECT
        item.value_id AS item_id,
        COALESCE(item_catalog.display, item.value_id::text) AS item_display,
        price_type.value_id AS price_type_id,
        COALESCE(price_type_catalog.display, price_type.value_id::text) AS price_type_display,
        COALESCE(latest.currency, '') AS currency,
        COALESCE(latest.unit_price, 0) AS unit_price,
        latest.effective_date,
        latest.source_document_id
    FROM latest
    JOIN platform_dimension_set_items item
      ON item.dimension_set_id = latest.dimension_set_id
     AND item.dimension_id = @ItemDimensionId
    JOIN platform_dimension_set_items price_type
      ON price_type.dimension_set_id = latest.dimension_set_id
     AND price_type.dimension_id = @PriceTypeDimensionId
    LEFT JOIN cat_trd_item item_catalog ON item_catalog.catalog_id = item.value_id
    LEFT JOIN cat_trd_price_type price_type_catalog ON price_type_catalog.catalog_id = price_type.value_id
    WHERE latest.is_deleted = FALSE
      AND (@HasItemFilter = FALSE OR item.value_id = ANY(@ItemIds))
      AND (@HasPriceTypeFilter = FALSE OR price_type.value_id = ANY(@PriceTypeIds))
)
SELECT
    item_id AS ItemId,
    item_display AS ItemDisplay,
    price_type_id AS PriceTypeId,
    price_type_display AS PriceTypeDisplay,
    currency AS Currency,
    unit_price AS UnitPrice,
    effective_date AS EffectiveDate,
    source_document_id AS SourceDocumentId,
    COUNT(*) OVER()::integer AS TotalCount
FROM enriched
ORDER BY LOWER(item_display), item_display, LOWER(price_type_display), price_type_display, LOWER(currency), currency,
         item_id, price_type_id
OFFSET @Offset
LIMIT @Limit;
""";

        var rows = (await uow.Connection.QueryAsync<CurrentItemPriceSqlRow>(new CommandDefinition(
            sql,
            new
            {
                ItemDimensionId,
                PriceTypeDimensionId,
                AsOfUtc = asOfUtc,
                HasItemFilter = itemIdArray.Length > 0,
                HasPriceTypeFilter = priceTypeIdArray.Length > 0,
                ItemIds = itemIdArray,
                PriceTypeIds = priceTypeIdArray,
                Offset = offset,
                Limit = limit
            },
            uow.Transaction,
            cancellationToken: ct))).AsList();

        return new TradeCurrentItemPricePage(
            rows.Select(static row => new TradeCurrentItemPriceRow(
                row.ItemId,
                row.ItemDisplay,
                row.PriceTypeId,
                row.PriceTypeDisplay,
                row.Currency,
                row.UnitPrice,
                row.EffectiveDate,
                row.SourceDocumentId)).ToArray(),
            rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    private static Guid[] NormalizeIds(IReadOnlyList<Guid>? ids)
        => ids?.Where(static id => id != Guid.Empty).Distinct().ToArray() ?? [];

    private sealed record CurrentItemPriceSqlRow(
        Guid ItemId,
        string ItemDisplay,
        Guid PriceTypeId,
        string PriceTypeDisplay,
        string Currency,
        decimal UnitPrice,
        DateOnly? EffectiveDate,
        Guid? SourceDocumentId,
        int TotalCount);
}
