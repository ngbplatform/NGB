namespace NGB.Trade.Documents;

public sealed record TradeVendorReturnHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    Guid VendorId,
    Guid WarehouseId,
    Guid? PurchaseReceiptId,
    string? Notes,
    decimal Amount);

public sealed record TradeVendorReturnLine(
    Guid DocumentId,
    int Ordinal,
    Guid ItemId,
    decimal Quantity,
    decimal UnitCost,
    decimal LineAmount);
