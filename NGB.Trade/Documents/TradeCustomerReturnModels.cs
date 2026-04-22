namespace NGB.Trade.Documents;

public sealed record TradeCustomerReturnHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    Guid CustomerId,
    Guid WarehouseId,
    Guid? SalesInvoiceId,
    string? Notes,
    decimal Amount);

public sealed record TradeCustomerReturnLine(
    Guid DocumentId,
    int Ordinal,
    Guid ItemId,
    decimal Quantity,
    decimal UnitPrice,
    decimal UnitCost,
    decimal LineAmount);
