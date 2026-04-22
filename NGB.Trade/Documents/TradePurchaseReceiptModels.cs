namespace NGB.Trade.Documents;

public sealed record TradePurchaseReceiptHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    Guid VendorId,
    Guid WarehouseId,
    string? Notes,
    decimal Amount);

public sealed record TradePurchaseReceiptLine(
    Guid DocumentId,
    int Ordinal,
    Guid ItemId,
    decimal Quantity,
    decimal UnitCost,
    decimal LineAmount);
