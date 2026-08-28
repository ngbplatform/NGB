using Dapper;
using NGB.Contracts.Common;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Trade.Reporting;

namespace NGB.Trade.PostgreSql.Reporting;

public sealed class PostgresTradeCurrentItemPriceReader(IUnitOfWork uow)
    : ITradeCurrentItemPriceReader
{
    private static readonly Guid ItemDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Item}");
    private static readonly Guid PriceTypeDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.PriceType}");
    private static readonly string ItemPricesTable = ReferenceRegisterNaming.RecordsTable(TradeCodes.ItemPricesRegisterCode);
    private bool? _itemPricesTableExists;

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

        if (!await ItemPricesTableExistsAsync(ct))
            return new TradeCurrentItemPricePage([], 0);

        var itemIdArray = NormalizeIds(itemIds);
        var priceTypeIdArray = NormalizeIds(priceTypeIds);
        var sql = $"""
WITH candidate_dimension_sets AS (
    SELECT
        item.dimension_set_id,
        item.value_id AS item_id,
        price_type.value_id AS price_type_id
    FROM platform_dimension_set_items item
    JOIN platform_dimension_set_items price_type
      ON price_type.dimension_set_id = item.dimension_set_id
     AND price_type.dimension_id = @PriceTypeDimensionId
    WHERE item.dimension_id = @ItemDimensionId
      AND (@HasItemFilter = FALSE OR item.value_id = ANY(@ItemIds))
      AND (@HasPriceTypeFilter = FALSE OR price_type.value_id = ANY(@PriceTypeIds))
),
latest AS (
    SELECT
        candidate.item_id,
        candidate.price_type_id,
        record.currency,
        record.unit_price,
        record.effective_date,
        record.source_document_id,
        record.is_deleted
    FROM candidate_dimension_sets candidate
    JOIN LATERAL (
        SELECT
            price.currency,
            price.unit_price,
            price.effective_date,
            price.source_document_id,
            price.is_deleted
        FROM {ItemPricesTable} price
        WHERE price.dimension_set_id = candidate.dimension_set_id
          AND price.recorder_document_id IS NULL
          AND price.recorded_at_utc <= @AsOfUtc
        ORDER BY price.recorded_at_utc DESC, price.record_id DESC
        LIMIT 1
    ) record ON TRUE
),
enriched AS (
    SELECT
        latest.item_id,
        COALESCE(item.display, latest.item_id::text) AS item_display,
        latest.price_type_id,
        COALESCE(price_type.display, latest.price_type_id::text) AS price_type_display,
        COALESCE(latest.currency, '') AS currency,
        latest.unit_price,
        latest.effective_date,
        latest.source_document_id
    FROM latest
    LEFT JOIN cat_trd_item item ON item.catalog_id = latest.item_id
    LEFT JOIN cat_trd_price_type price_type ON price_type.catalog_id = latest.price_type_id
    WHERE latest.is_deleted = FALSE
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
                Offset = PagingLimits.BoundOffset(offset),
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

    private async Task<bool> ItemPricesTableExistsAsync(CancellationToken ct)
    {
        if (_itemPricesTableExists == true)
            return true;

        var exists = await uow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT to_regclass(@TableName) IS NOT NULL;",
            new { TableName = ItemPricesTable },
            uow.Transaction,
            cancellationToken: ct));

        if (exists)
            _itemPricesTableExists = true;

        return exists;
    }

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
