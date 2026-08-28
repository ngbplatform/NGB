using Dapper;
using NGB.Core.Dimensions;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.Tools.Extensions;
using NGB.Trade.Pricing;

namespace NGB.Trade.PostgreSql.Pricing;

public sealed class TradePricingLookupReader(IUnitOfWork uow) : ITradePricingLookupReader
{
    private static readonly string ItemPricesTable = ReferenceRegisterNaming.RecordsTable(TradeCodes.ItemPricesRegisterCode);
    private bool? _itemPricesTableExists;

    public async Task<IReadOnlyDictionary<Guid, TradeItemSalesProfile>> GetItemSalesProfilesAsync(
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken ct = default)
    {
        if (itemIds.Count == 0)
            return new Dictionary<Guid, TradeItemSalesProfile>();

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    i.catalog_id AS ItemId,
    i.default_sales_price_type_id AS DefaultSalesPriceTypeId,
    pt.display AS DefaultSalesPriceTypeDisplay
FROM cat_trd_item i
LEFT JOIN cat_trd_price_type pt
    ON pt.catalog_id = i.default_sales_price_type_id
WHERE i.catalog_id = ANY(@ItemIds);
""";

        var rows = await uow.Connection.QueryAsync<ItemSalesProfileRow>(
            new CommandDefinition(
                sql,
                new { ItemIds = itemIds.Distinct().ToArray() },
                transaction: uow.Transaction,
                cancellationToken: ct));

        return rows.ToDictionary(
            static row => row.ItemId,
            static row => new TradeItemSalesProfile(row.ItemId, row.DefaultSalesPriceTypeId, row.DefaultSalesPriceTypeDisplay));
    }

    public async Task<IReadOnlyDictionary<TradePriceLookupKey, TradeItemPriceSnapshot>> GetLatestItemPricesAsync(
        IReadOnlyCollection<TradePriceLookupKey> keys,
        DateOnly asOfDate,
        CancellationToken ct = default)
    {
        if (keys.Count == 0)
            return new Dictionary<TradePriceLookupKey, TradeItemPriceSnapshot>();

        await uow.EnsureConnectionOpenAsync(ct);

        if (!await ItemPricesTableExistsAsync(ct))
            return new Dictionary<TradePriceLookupKey, TradeItemPriceSnapshot>();

        var distinctKeys = keys.Distinct().ToArray();
        var itemIds = new Guid[distinctKeys.Length];
        var priceTypeIds = new Guid[distinctKeys.Length];
        var dimensionSetIds = new Guid[distinctKeys.Length];

        for (var i = 0; i < distinctKeys.Length; i++)
        {
            var key = distinctKeys[i];
            itemIds[i] = key.ItemId;
            priceTypeIds[i] = key.PriceTypeId;
            dimensionSetIds[i] = BuildPriceDimensionSetId(key.ItemId, key.PriceTypeId);
        }

        var sql = $"""
WITH requested AS (
    SELECT DISTINCT
        q.item_id,
        q.price_type_id,
        q.dimension_set_id
    FROM unnest(@ItemIds::uuid[], @PriceTypeIds::uuid[], @DimensionSetIds::uuid[])
        AS q(item_id, price_type_id, dimension_set_id)
)
SELECT
    requested.item_id AS ItemId,
    requested.price_type_id AS PriceTypeId,
    latest.unit_price AS UnitPrice,
    latest.currency AS Currency,
    latest.effective_date AS EffectiveDate,
    latest.source_document_id AS SourceDocumentId,
    latest.is_deleted AS IsDeleted
FROM requested
LEFT JOIN LATERAL (
    SELECT
        record.unit_price,
        record.currency,
        record.effective_date,
        record.source_document_id,
        record.is_deleted
    FROM {ItemPricesTable} record
    WHERE record.dimension_set_id = requested.dimension_set_id
      AND record.recorder_document_id IS NULL
      AND record.effective_date IS NOT NULL
      AND record.effective_date <= @AsOfDate::date
    ORDER BY
        record.effective_date DESC,
        record.recorded_at_utc DESC,
        record.record_id DESC
    LIMIT 1
) latest ON TRUE;
""";

        var rows = await uow.Connection.QueryAsync<ItemPriceSnapshotRow>(
            new CommandDefinition(
                sql,
                new
                {
                    ItemIds = itemIds,
                    PriceTypeIds = priceTypeIds,
                    DimensionSetIds = dimensionSetIds,
                    AsOfDate = asOfDate,
                },
                transaction: uow.Transaction,
                cancellationToken: ct));

        var result = new Dictionary<TradePriceLookupKey, TradeItemPriceSnapshot>();
        foreach (var row in rows)
        {
            var snapshot = MapItemPriceRow(
                row.ItemId,
                row.PriceTypeId,
                row.UnitPrice,
                row.Currency,
                row.EffectiveDate,
                row.SourceDocumentId,
                row.IsDeleted);
            if (snapshot is null)
                continue;

            result[new TradePriceLookupKey(row.ItemId, row.PriceTypeId)] = snapshot;
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<TradeWarehouseItemKey, decimal>> GetLatestUnitCostsAsync(
        IReadOnlyCollection<TradeWarehouseItemKey> keys,
        DateOnly asOfDate,
        CancellationToken ct = default)
    {
        if (keys.Count == 0)
            return new Dictionary<TradeWarehouseItemKey, decimal>();

        await uow.EnsureConnectionOpenAsync(ct);

        var distinctKeys = keys.Distinct().ToArray();
        var warehouseIds = new Guid[distinctKeys.Length];
        var itemIds = new Guid[distinctKeys.Length];

        for (var i = 0; i < distinctKeys.Length; i++)
        {
            warehouseIds[i] = distinctKeys[i].WarehouseId;
            itemIds[i] = distinctKeys[i].ItemId;
        }

        const string sql = """
WITH requested AS (
    SELECT DISTINCT
        q.warehouse_id,
        q.item_id
    FROM unnest(@WarehouseIds::uuid[], @ItemIds::uuid[]) AS q(warehouse_id, item_id)
),
candidates AS (
    SELECT
        r.warehouse_id AS WarehouseId,
        r.item_id AS ItemId,
        l.unit_cost AS UnitCost,
        h.document_date_utc AS DocumentDateUtc,
        d.posted_at_utc AS PostedAtUtc,
        h.document_id AS DocumentId,
        l.ordinal AS Ordinal
    FROM requested r
    JOIN doc_trd_purchase_receipt h
        ON h.warehouse_id = r.warehouse_id
       AND h.document_date_utc <= @AsOfDate::date
    JOIN documents d
        ON d.id = h.document_id
       AND d.status = 2
    JOIN doc_trd_purchase_receipt__lines l
        ON l.document_id = h.document_id
       AND l.item_id = r.item_id

    UNION ALL

    SELECT
        r.warehouse_id AS WarehouseId,
        r.item_id AS ItemId,
        l.unit_cost AS UnitCost,
        h.document_date_utc AS DocumentDateUtc,
        d.posted_at_utc AS PostedAtUtc,
        h.document_id AS DocumentId,
        l.ordinal AS Ordinal
    FROM requested r
    JOIN doc_trd_sales_invoice h
        ON h.warehouse_id = r.warehouse_id
       AND h.document_date_utc <= @AsOfDate::date
    JOIN documents d
        ON d.id = h.document_id
       AND d.status = 2
    JOIN doc_trd_sales_invoice__lines l
        ON l.document_id = h.document_id
       AND l.item_id = r.item_id

    UNION ALL

    SELECT
        r.warehouse_id AS WarehouseId,
        r.item_id AS ItemId,
        l.unit_cost AS UnitCost,
        h.document_date_utc AS DocumentDateUtc,
        d.posted_at_utc AS PostedAtUtc,
        h.document_id AS DocumentId,
        l.ordinal AS Ordinal
    FROM requested r
    JOIN doc_trd_customer_return h
        ON h.warehouse_id = r.warehouse_id
       AND h.document_date_utc <= @AsOfDate::date
    JOIN documents d
        ON d.id = h.document_id
       AND d.status = 2
    JOIN doc_trd_customer_return__lines l
        ON l.document_id = h.document_id
       AND l.item_id = r.item_id

    UNION ALL

    SELECT
        r.warehouse_id AS WarehouseId,
        r.item_id AS ItemId,
        l.unit_cost AS UnitCost,
        h.document_date_utc AS DocumentDateUtc,
        d.posted_at_utc AS PostedAtUtc,
        h.document_id AS DocumentId,
        l.ordinal AS Ordinal
    FROM requested r
    JOIN doc_trd_vendor_return h
        ON h.warehouse_id = r.warehouse_id
       AND h.document_date_utc <= @AsOfDate::date
    JOIN documents d
        ON d.id = h.document_id
       AND d.status = 2
    JOIN doc_trd_vendor_return__lines l
        ON l.document_id = h.document_id
       AND l.item_id = r.item_id

    UNION ALL

    SELECT
        r.warehouse_id AS WarehouseId,
        r.item_id AS ItemId,
        l.unit_cost AS UnitCost,
        h.document_date_utc AS DocumentDateUtc,
        d.posted_at_utc AS PostedAtUtc,
        h.document_id AS DocumentId,
        l.ordinal AS Ordinal
    FROM requested r
    JOIN doc_trd_inventory_adjustment h
        ON h.warehouse_id = r.warehouse_id
       AND h.document_date_utc <= @AsOfDate::date
    JOIN documents d
        ON d.id = h.document_id
       AND d.status = 2
    JOIN doc_trd_inventory_adjustment__lines l
        ON l.document_id = h.document_id
       AND l.item_id = r.item_id
),
latest AS (
    SELECT DISTINCT ON (WarehouseId, ItemId)
        WarehouseId,
        ItemId,
        UnitCost
    FROM candidates
    ORDER BY
        WarehouseId,
        ItemId,
        DocumentDateUtc DESC,
        PostedAtUtc DESC NULLS LAST,
        DocumentId DESC,
        Ordinal DESC
)
SELECT
    WarehouseId,
    ItemId,
    UnitCost
FROM latest;
""";

        var rows = await uow.Connection.QueryAsync<UnitCostRow>(
            new CommandDefinition(
                sql,
                new
                {
                    WarehouseIds = warehouseIds,
                    ItemIds = itemIds,
                    AsOfDate = asOfDate,
                },
                transaction: uow.Transaction,
                cancellationToken: ct));

        return rows.ToDictionary(
            static row => new TradeWarehouseItemKey(row.WarehouseId, row.ItemId),
            static row => row.UnitCost);
    }

    internal static TradeItemPriceSnapshot? MapItemPriceRow(
        Guid itemId,
        Guid priceTypeId,
        decimal? unitPrice,
        string? currency,
        DateOnly? effectiveDate,
        Guid? sourceDocumentId,
        bool? isDeleted)
    {
        if (unitPrice is not { } requiredUnitPrice
            || effectiveDate is not { } requiredEffectiveDate
            || isDeleted == true)
        {
            return null;
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency)
            ? TradeCodes.DefaultCurrency
            : currency.Trim().ToUpperInvariant();

        return new TradeItemPriceSnapshot(
            itemId,
            priceTypeId,
            requiredUnitPrice,
            normalizedCurrency,
            requiredEffectiveDate,
            sourceDocumentId);
    }

    private static Guid BuildPriceDimensionSetId(Guid itemId, Guid priceTypeId)
    {
        var bag = new DimensionBag(
        [
            new DimensionValue(DeterministicGuid.Create($"Dimension|{TradeCodes.Item}"), itemId),
            new DimensionValue(DeterministicGuid.Create($"Dimension|{TradeCodes.PriceType}"), priceTypeId),
        ]);

        return DeterministicDimensionSetId.FromBag(bag);
    }

    private async Task<bool> ItemPricesTableExistsAsync(CancellationToken ct)
    {
        if (_itemPricesTableExists == true)
            return true;

        var exists = await uow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT to_regclass(@TableName) IS NOT NULL;",
            new { TableName = ItemPricesTable },
            transaction: uow.Transaction,
            cancellationToken: ct));

        if (exists)
            _itemPricesTableExists = true;

        return exists;
    }

    private sealed record ItemSalesProfileRow(
        Guid ItemId,
        Guid? DefaultSalesPriceTypeId,
        string? DefaultSalesPriceTypeDisplay);

    private sealed record ItemPriceSnapshotRow(
        Guid ItemId,
        Guid PriceTypeId,
        decimal? UnitPrice,
        string? Currency,
        DateOnly? EffectiveDate,
        Guid? SourceDocumentId,
        bool? IsDeleted);

    private sealed record UnitCostRow(Guid WarehouseId, Guid ItemId, decimal UnitCost);
}
