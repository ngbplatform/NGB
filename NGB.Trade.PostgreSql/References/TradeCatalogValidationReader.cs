using Dapper;
using NGB.Persistence.UnitOfWork;
using NGB.Trade.References;

namespace NGB.Trade.PostgreSql.References;

public sealed class TradeCatalogValidationReader(IUnitOfWork uow) : ITradeCatalogValidationReader
{
    public async Task<IReadOnlyDictionary<Guid, TradeInventoryItemValidationSnapshot>> GetInventoryItemsAsync(
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken ct = default)
    {
        var ids = itemIds.Where(static id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, TradeInventoryItemValidationSnapshot>();

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               c.id AS "ItemId",
                               c.is_deleted AS "IsDeleted",
                               h.is_active AS "IsActive",
                               h.is_inventory_item AS "IsInventoryItem"
                           FROM catalogs c
                           LEFT JOIN cat_trd_item h
                             ON h.catalog_id = c.id
                           WHERE c.catalog_code = @CatalogCode
                             AND c.id = ANY(@Ids);
                           """;

        var rows = await uow.Connection.QueryAsync<TradeInventoryItemValidationSnapshot>(
            new CommandDefinition(
                sql,
                new { CatalogCode = TradeCodes.Item, Ids = ids },
                transaction: uow.Transaction,
                cancellationToken: ct));

        return rows.ToDictionary(static row => row.ItemId);
    }
}
