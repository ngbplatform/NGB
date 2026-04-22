namespace NGB.Trade.Documents;

public sealed record TradeItemPriceUpdateHead(Guid DocumentId, DateOnly EffectiveDate, string? Notes);

public sealed record TradeItemPriceUpdateLine(
    Guid DocumentId,
    int Ordinal,
    Guid ItemId,
    Guid PriceTypeId,
    string Currency,
    decimal UnitPrice);
