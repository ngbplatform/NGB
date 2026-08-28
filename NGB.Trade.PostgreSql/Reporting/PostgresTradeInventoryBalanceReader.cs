using Dapper;
using NGB.Contracts.Common;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Trade.Reporting;

namespace NGB.Trade.PostgreSql.Reporting;

public sealed class PostgresTradeInventoryBalanceReader(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceRepository resources)
    : ITradeInventoryBalanceReader
{
    private static readonly Guid ItemDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Item}");
    private static readonly Guid WarehouseDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Warehouse}");

    public async Task<TradeInventoryBalancePage> GetPageAsync(
        Guid registerId,
        DateOnly asOfInclusive,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? warehouseIds,
        TradeInventoryBalanceSort sort,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        if (!Enum.IsDefined(sort))
            throw new NgbArgumentOutOfRangeException(nameof(sort), sort, "Unknown inventory balance sort.");

        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Offset must be zero or greater.");

        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than zero.");

        await uow.EnsureConnectionOpenAsync(ct);

        var register = await registers.GetByIdAsync(registerId, ct)
            ?? throw new OperationalRegisterNotFoundException(registerId);
        
        var resourceColumns = (await resources.GetByRegisterIdAsync(registerId, ct))
            .Select(static resource => resource.ColumnCode)
            .ToHashSet(StringComparer.Ordinal);

        if (!resourceColumns.Contains("qty_delta"))
            throw new NgbConfigurationViolationException($"Operational register '{registerId}' does not define resource column 'qty_delta'.");

        var tableName = OperationalRegisterNaming.MovementsTable(register.TableCode);
        var balancesTableName = OperationalRegisterNaming.BalancesTable(register.TableCode);
        var exists = await uow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT to_regclass(@TableName) IS NOT NULL;",
            new { TableName = tableName },
            uow.Transaction,
            cancellationToken: ct));

        if (!exists)
            return new TradeInventoryBalancePage([], 0, 0m);

        var balancesExist = await uow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT to_regclass(@TableName) IS NOT NULL;",
            new { TableName = balancesTableName },
            uow.Transaction,
            cancellationToken: ct));

        var itemIdArray = NormalizeIds(itemIds);
        var warehouseIdArray = NormalizeIds(warehouseIds);
        var occurredToExclusiveUtc = asOfInclusive == DateOnly.MaxValue
            ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)
            : DateTime.SpecifyKind(asOfInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var orderBy = sort == TradeInventoryBalanceSort.AbsoluteQuantityDescending
            ? "ABS(position.quantity) DESC, item_display ASC, warehouse_display ASC, position.item_id, position.warehouse_id"
            : "item_display ASC, warehouse_display ASC, position.item_id, position.warehouse_id";

        var positionSourceSql = balancesExist
            ? BuildSnapshotBackedPositionSourceSql(tableName, balancesTableName)
            : BuildMovementOnlyPositionSourceSql(tableName);
        var sql = $"""
{positionSourceSql},
positions AS (
    SELECT
        item.value_id AS item_id,
        warehouse.value_id AS warehouse_id,
        SUM(source.quantity) AS quantity
    FROM dimension_positions source
    JOIN platform_dimension_set_items item
      ON item.dimension_set_id = source.dimension_set_id
     AND item.dimension_id = @ItemDimensionId
    JOIN platform_dimension_set_items warehouse
      ON warehouse.dimension_set_id = source.dimension_set_id
     AND warehouse.dimension_id = @WarehouseDimensionId
    WHERE (@HasItemFilter = FALSE OR item.value_id = ANY(@ItemIds))
      AND (@HasWarehouseFilter = FALSE OR warehouse.value_id = ANY(@WarehouseIds))
    GROUP BY item.value_id, warehouse.value_id
    HAVING SUM(source.quantity) <> 0
),
enriched AS (
    SELECT
        position.item_id,
        COALESCE(item.display, position.item_id::text) AS item_display,
        position.warehouse_id,
        COALESCE(warehouse.display, position.warehouse_id::text) AS warehouse_display,
        position.quantity
    FROM positions position
    LEFT JOIN cat_trd_item item ON item.catalog_id = position.item_id
    LEFT JOIN cat_trd_warehouse warehouse ON warehouse.catalog_id = position.warehouse_id
)
SELECT
    position.item_id AS ItemId,
    position.item_display AS ItemDisplay,
    position.warehouse_id AS WarehouseId,
    position.warehouse_display AS WarehouseDisplay,
    position.quantity AS Quantity,
    COUNT(*) OVER()::integer AS TotalCount,
    SUM(position.quantity) OVER() AS TotalQuantity
FROM enriched position
ORDER BY {orderBy}
OFFSET @Offset
LIMIT @Limit;
""";

        var rows = (await uow.Connection.QueryAsync<InventoryBalanceSqlRow>(new CommandDefinition(
            sql,
            new
            {
                ItemDimensionId,
                WarehouseDimensionId,
                AsOfMonth = new DateOnly(asOfInclusive.Year, asOfInclusive.Month, 1),
                OccurredToExclusiveUtc = occurredToExclusiveUtc,
                HasItemFilter = itemIdArray.Length > 0,
                HasWarehouseFilter = warehouseIdArray.Length > 0,
                ItemIds = itemIdArray,
                WarehouseIds = warehouseIdArray,
                Offset = PagingLimits.BoundOffset(offset),
                Limit = limit
            },
            uow.Transaction,
            cancellationToken: ct))).AsList();

        var first = rows.FirstOrDefault();

        return new TradeInventoryBalancePage(
            rows.Select(static row => new TradeInventoryBalanceRow(
                row.ItemId,
                row.ItemDisplay,
                row.WarehouseId,
                row.WarehouseDisplay,
                row.Quantity)).ToArray(),
            first?.TotalCount ?? 0,
            first?.TotalQuantity ?? 0m);
    }

    private static string BuildMovementOnlyPositionSourceSql(string movementsTable) => $"""
WITH dimension_positions AS (
    SELECT
        movement.dimension_set_id,
        SUM(CASE WHEN movement.is_storno THEN -movement.qty_delta ELSE movement.qty_delta END) AS quantity
    FROM {movementsTable} movement
    WHERE movement.period_month <= @AsOfMonth
      AND movement.occurred_at_utc < @OccurredToExclusiveUtc
    GROUP BY movement.dimension_set_id
)
""";

    private static string BuildSnapshotBackedPositionSourceSql(string movementsTable, string balancesTable) => $"""
WITH latest_snapshot AS (
    SELECT MAX(period_month) AS period_month
    FROM {balancesTable}
    WHERE period_month < @AsOfMonth
),
opening AS (
    SELECT balance.dimension_set_id, balance.qty_delta AS quantity
    FROM {balancesTable} balance
    CROSS JOIN latest_snapshot latest
    WHERE balance.period_month = latest.period_month
),
movement_delta AS (
    SELECT
        movement.dimension_set_id,
        SUM(CASE WHEN movement.is_storno THEN -movement.qty_delta ELSE movement.qty_delta END) AS quantity
    FROM {movementsTable} movement
    CROSS JOIN latest_snapshot latest
    WHERE (latest.period_month IS NULL OR movement.period_month > latest.period_month)
      AND movement.period_month <= @AsOfMonth
      AND movement.occurred_at_utc < @OccurredToExclusiveUtc
    GROUP BY movement.dimension_set_id
),
dimension_positions AS (
    SELECT
        keys.dimension_set_id,
        COALESCE(opening.quantity, 0) + COALESCE(delta.quantity, 0) AS quantity
    FROM (
        SELECT dimension_set_id FROM opening
        UNION
        SELECT dimension_set_id FROM movement_delta
    ) keys
    LEFT JOIN opening ON opening.dimension_set_id = keys.dimension_set_id
    LEFT JOIN movement_delta delta ON delta.dimension_set_id = keys.dimension_set_id
)
""";

    private static Guid[] NormalizeIds(IReadOnlyList<Guid>? ids)
        => ids?.Where(static id => id != Guid.Empty).Distinct().ToArray() ?? [];

    private sealed record InventoryBalanceSqlRow(
        Guid ItemId,
        string ItemDisplay,
        Guid WarehouseId,
        string WarehouseDisplay,
        decimal Quantity,
        int TotalCount,
        decimal TotalQuantity);
}
