namespace NGB.Trade.Documents;

public sealed record TradeInventoryTransferHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    string? Notes);

public sealed record TradeInventoryTransferLine(
    Guid DocumentId,
    int Ordinal,
    Guid ItemId,
    decimal Quantity);
