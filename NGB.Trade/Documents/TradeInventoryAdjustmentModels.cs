namespace NGB.Trade.Documents;

public sealed record TradeInventoryAdjustmentHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    Guid WarehouseId,
    Guid ReasonId,
    string? Notes,
    decimal Amount);

public sealed record TradeInventoryAdjustmentLine(
    Guid DocumentId,
    int Ordinal,
    Guid ItemId,
    decimal QuantityDelta,
    decimal UnitCost,
    decimal LineAmount);
