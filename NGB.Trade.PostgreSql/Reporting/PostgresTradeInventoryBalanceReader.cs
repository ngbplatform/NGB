using Dapper;
using NGB.Contracts.Common;
using NGB.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.OperationalRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Trade.Reporting;

namespace NGB.Trade.PostgreSql.Reporting;

public sealed class PostgresTradeInventoryBalanceReader(IUnitOfWork uow, OperationalRegisterReadContextCache contextCache)
    : ITradeInventoryBalanceReader
{
    private static readonly Guid ItemDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Item}");
    private static readonly Guid WarehouseDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Warehouse}");

    public Task<TradeInventoryBalancePage> GetPageAsync(
        Guid registerId,
        DateOnly asOfInclusive,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? warehouseIds,
        TradeInventoryBalanceSort sort,
        int offset,
        int limit,
        CancellationToken ct = default)
        => GetPageCoreAsync(
            registerId, asOfInclusive, itemIds, warehouseIds, sort, offset, limit,
            cursor: null, cursorMode: false, ct);

    public Task<TradeInventoryBalancePage> GetCursorPageAsync(
        Guid registerId,
        DateOnly asOfInclusive,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? warehouseIds,
        TradeInventoryBalanceSort sort,
        TradeInventoryBalancePageCursor? cursor,
        int limit,
        CancellationToken ct = default)
        => GetPageCoreAsync(
            registerId, asOfInclusive, itemIds, warehouseIds, sort, cursor?.Offset ?? 0, limit,
            cursor, cursorMode: true, ct);

    private async Task<TradeInventoryBalancePage> GetPageCoreAsync(
        Guid registerId,
        DateOnly asOfInclusive,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? warehouseIds,
        TradeInventoryBalanceSort sort,
        int offset,
        int limit,
        TradeInventoryBalancePageCursor? cursor,
        bool cursorMode,
        CancellationToken ct)
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

        var context = await GetRegisterContextAsync(registerId, ct);
        if (!context.MovementsExist)
            return new TradeInventoryBalancePage([], 0, 0m);

        var itemIdArray = NormalizeIds(itemIds);
        var warehouseIdArray = NormalizeIds(warehouseIds);
        var occurredToExclusiveUtc = asOfInclusive == DateOnly.MaxValue
            ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)
            : DateTime.SpecifyKind(asOfInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var orderBy = sort == TradeInventoryBalanceSort.AbsoluteQuantityDescending
            ? "ABS(position.quantity) DESC, item_display ASC, warehouse_display ASC, position.item_id, position.warehouse_id"
            : "item_display ASC, warehouse_display ASC, position.item_id, position.warehouse_id";
        var hasCommonSeekKey = cursor is
        {
            AfterItemDisplay: not null,
            AfterWarehouseDisplay: not null,
            AfterItemId: not null,
            AfterWarehouseId: not null
        };
        var useSeek = hasCommonSeekKey
                      && (sort != TradeInventoryBalanceSort.AbsoluteQuantityDescending
                          || cursor?.AfterAbsoluteQuantity is not null);
        var seekSql = !useSeek
            ? string.Empty
            : sort == TradeInventoryBalanceSort.AbsoluteQuantityDescending
                ? """
                  WHERE ABS(position.quantity) < @AfterAbsoluteQuantity::numeric
                     OR (ABS(position.quantity) = @AfterAbsoluteQuantity::numeric
                         AND (position.item_display, position.warehouse_display, position.item_id, position.warehouse_id)
                           > (@AfterItemDisplay::text, @AfterWarehouseDisplay::text, @AfterItemId::uuid, @AfterWarehouseId::uuid))
                  """
                : """
                  WHERE (position.item_display, position.warehouse_display, position.item_id, position.warehouse_id)
                      > (@AfterItemDisplay::text, @AfterWarehouseDisplay::text, @AfterItemId::uuid, @AfterWarehouseId::uuid)
                  """;
        var offsetSql = useSeek ? string.Empty : "OFFSET @Offset";

        var positionSourceSql = context.BalancesExist
            ? BuildSnapshotBackedPositionSourceSql(context.MovementsTable, context.BalancesTable)
            : BuildMovementOnlyPositionSourceSql(context.MovementsTable);
        var totalsProjection = cursor is null
            ? "COUNT(*) OVER()::integer AS TotalCount, SUM(position.quantity) OVER() AS TotalQuantity"
            : "@KnownTotal::integer AS TotalCount, @KnownTotalQuantity::numeric AS TotalQuantity";
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
    {totalsProjection}
FROM enriched position
{seekSql}
ORDER BY {orderBy}
{offsetSql}
LIMIT @QueryLimit;
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
                QueryLimit = cursorMode && limit < int.MaxValue ? limit + 1 : limit,
                KnownTotal = cursor?.Total,
                KnownTotalQuantity = cursor?.TotalQuantity,
                AfterAbsoluteQuantity = cursor?.AfterAbsoluteQuantity,
                AfterItemDisplay = cursor?.AfterItemDisplay,
                AfterWarehouseDisplay = cursor?.AfterWarehouseDisplay,
                AfterItemId = cursor?.AfterItemId,
                AfterWarehouseId = cursor?.AfterWarehouseId
            },
            uow.Transaction,
            cancellationToken: ct))).AsList();

        var first = rows.FirstOrDefault();
        var hasMore = cursorMode && rows.Count > limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var last = rows.LastOrDefault();

        return new TradeInventoryBalancePage(
            rows.Select(static row => new TradeInventoryBalanceRow(
                row.ItemId,
                row.ItemDisplay,
                row.WarehouseId,
                row.WarehouseDisplay,
                row.Quantity)).ToArray(),
            cursor?.Total ?? first?.TotalCount ?? 0,
            cursor?.TotalQuantity ?? first?.TotalQuantity ?? 0m,
            hasMore,
            last is null ? null : Math.Abs(last.Quantity),
            last?.ItemDisplay,
            last?.WarehouseDisplay,
            last?.ItemId,
            last?.WarehouseId);
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

    private Task<OperationalRegisterReadContext> GetRegisterContextAsync(Guid registerId, CancellationToken ct)
        => contextCache.GetOrCreateAsync(
            registerId,
            "qty_delta",
            loadCt => LoadRegisterContextAsync(registerId, loadCt),
            ct);

    private async Task<OperationalRegisterReadContext> LoadRegisterContextAsync(Guid registerId, CancellationToken ct)
    {
        const string sql = """
SELECT
    r.table_code AS TableCode,
    EXISTS (
        SELECT 1
        FROM operational_register_resources resource
        WHERE resource.register_id = r.register_id
          AND resource.column_code = 'qty_delta'
    ) AS HasRequiredResource,
    to_regclass('opreg_' || r.table_code || '__movements') IS NOT NULL AS MovementsExist,
    to_regclass('opreg_' || r.table_code || '__balances') IS NOT NULL AS BalancesExist
FROM operational_registers r
WHERE r.register_id = @RegisterId;
""";
        var row = await uow.Connection.QuerySingleOrDefaultAsync<RegisterContextSqlRow>(new CommandDefinition(
            sql,
            new { RegisterId = registerId },
            uow.Transaction,
            cancellationToken: ct));

        if (row is null)
            throw new NGB.OperationalRegisters.Exceptions.OperationalRegisterNotFoundException(registerId);

        if (!row.HasRequiredResource)
            throw new NgbConfigurationViolationException($"Operational register '{registerId}' does not define resource column 'qty_delta'.");

        return new OperationalRegisterReadContext(
            OperationalRegisterNaming.MovementsTable(row.TableCode),
            OperationalRegisterNaming.BalancesTable(row.TableCode),
            row.MovementsExist,
            row.BalancesExist);
    }

    private sealed record RegisterContextSqlRow(
        string TableCode,
        bool HasRequiredResource,
        bool MovementsExist,
        bool BalancesExist);

    private sealed record InventoryBalanceSqlRow(
        Guid ItemId,
        string ItemDisplay,
        Guid WarehouseId,
        string WarehouseDisplay,
        decimal Quantity,
        int TotalCount,
        decimal TotalQuantity);
}
