namespace NGB.Trade.Reporting;

public enum TradeInventoryBalanceSort
{
    ItemWarehouse = 0,
    AbsoluteQuantityDescending = 1
}

public sealed record TradeInventoryBalanceRow(
    Guid ItemId,
    string ItemDisplay,
    Guid WarehouseId,
    string WarehouseDisplay,
    decimal Quantity);

public sealed record TradeInventoryBalancePage(
    IReadOnlyList<TradeInventoryBalanceRow> Rows,
    int Total,
    decimal TotalQuantity,
    bool HasMore = false);

public sealed record TradeInventoryBalancePageCursor(int Offset, int Total, decimal TotalQuantity);

public interface ITradeInventoryBalanceReader
{
    Task<TradeInventoryBalancePage> GetPageAsync(
        Guid registerId,
        DateOnly asOfInclusive,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? warehouseIds,
        TradeInventoryBalanceSort sort,
        int offset,
        int limit,
        CancellationToken ct = default);

    async Task<TradeInventoryBalancePage> GetCursorPageAsync(
        Guid registerId,
        DateOnly asOfInclusive,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? warehouseIds,
        TradeInventoryBalanceSort sort,
        TradeInventoryBalancePageCursor? cursor,
        int limit,
        CancellationToken ct = default)
    {
        var offset = cursor?.Offset ?? 0;
        var page = await GetPageAsync(registerId, asOfInclusive, itemIds, warehouseIds, sort, offset, limit, ct);
        return page with { HasMore = offset + page.Rows.Count < page.Total };
    }
}
