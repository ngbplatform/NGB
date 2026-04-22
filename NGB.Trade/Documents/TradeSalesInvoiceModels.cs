namespace NGB.Trade.Documents;

public sealed record TradeSalesInvoiceHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    Guid CustomerId,
    Guid WarehouseId,
    Guid? PriceTypeId,
    string? Notes,
    decimal Amount);

public sealed record TradeSalesInvoiceLine(
    Guid DocumentId,
    int Ordinal,
    Guid ItemId,
    decimal Quantity,
    decimal UnitPrice,
    decimal UnitCost,
    decimal LineAmount);
