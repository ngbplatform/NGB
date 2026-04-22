using Dapper;
using NGB.Core.Dimensions;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Trade.Pricing;

namespace NGB.Trade.PostgreSql.Pricing;

public sealed class TradePricingLookupReader(
    IUnitOfWork uow,
    IReferenceRegisterRepository referenceRegisters)
    : ITradePricingLookupReader
{
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

        var register = await referenceRegisters.GetByCodeAsync(TradeCodes.ItemPricesRegisterCode, ct)
            ?? throw new NgbConfigurationViolationException($"Reference register '{TradeCodes.ItemPricesRegisterCode}' is not configured.");

        var table = ReferenceRegisterNaming.RecordsTable(register.TableCode);

        if (!await RecordsTableExistsAsync(table, ct))
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
    FROM unnest(@ItemIds::uuid[], @PriceTypeIds::uuid[], @DimensionSetIds::uuid[]) AS q(item_id, price_type_id, dimension_set_id)
),
latest AS (
    SELECT DISTINCT ON (r.item_id, r.price_type_id)
        r.item_id AS ItemId,
        r.price_type_id AS PriceTypeId,
        t.unit_price AS UnitPrice,
        t.currency AS Currency,
        t.effective_date AS EffectiveDate,
        t.source_document_id AS SourceDocumentId,
        t.is_deleted AS IsDeleted
    FROM requested r
    LEFT JOIN {table} t
        ON t.dimension_set_id = r.dimension_set_id
       AND t.effective_date IS NOT NULL
       AND t.effective_date <= @AsOfDate::date
    ORDER BY
        r.item_id,
        r.price_type_id,
        t.effective_date DESC NULLS LAST,
        t.recorded_at_utc DESC NULLS LAST,
        t.record_id DESC NULLS LAST
)
SELECT
    ItemId,
    PriceTypeId,
    UnitPrice,
    Currency,
    EffectiveDate,
    SourceDocumentId,
    IsDeleted
FROM latest;
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
            if (row.UnitPrice is not { } unitPrice || row.EffectiveDate is not { } effectiveDate || row.IsDeleted == true)
                continue;

            var currency = string.IsNullOrWhiteSpace(row.Currency)
                ? TradeCodes.DefaultCurrency
                : row.Currency.Trim().ToUpperInvariant();

            result[new TradePriceLookupKey(row.ItemId, row.PriceTypeId)] = new TradeItemPriceSnapshot(
                row.ItemId,
                row.PriceTypeId,
                unitPrice,
                currency,
                effectiveDate,
                row.SourceDocumentId);
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

    private static Guid BuildPriceDimensionSetId(Guid itemId, Guid priceTypeId)
    {
        var bag = new DimensionBag(
        [
            new DimensionValue(DeterministicGuid.Create($"Dimension|{TradeCodes.Item}"), itemId),
            new DimensionValue(DeterministicGuid.Create($"Dimension|{TradeCodes.PriceType}"), priceTypeId),
        ]);

        return DeterministicDimensionSetId.FromBag(bag);
    }

    private async Task<bool> RecordsTableExistsAsync(string tableName, CancellationToken ct)
    {
        const string sql = """
SELECT EXISTS (
    SELECT 1
    FROM information_schema.tables
    WHERE table_schema = 'public'
      AND table_name = @TableName
);
""";

        return await uow.Connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new { TableName = tableName },
                transaction: uow.Transaction,
                cancellationToken: ct));
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
