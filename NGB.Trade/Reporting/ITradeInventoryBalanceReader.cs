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
    decimal TotalQuantity);

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
}
