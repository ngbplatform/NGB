namespace NGB.Trade.Pricing;

public readonly record struct TradePriceLookupKey(Guid ItemId, Guid PriceTypeId);

public readonly record struct TradeWarehouseItemKey(Guid WarehouseId, Guid ItemId);

public sealed record TradeItemSalesProfile(
    Guid ItemId,
    Guid? DefaultSalesPriceTypeId,
    string? DefaultSalesPriceTypeDisplay);

public sealed record TradeItemPriceSnapshot(
    Guid ItemId,
    Guid PriceTypeId,
    decimal UnitPrice,
    string Currency,
    DateOnly EffectiveDate,
    Guid? SourceDocumentId);

public interface ITradePricingLookupReader
{
    Task<IReadOnlyDictionary<Guid, TradeItemSalesProfile>> GetItemSalesProfilesAsync(
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<TradePriceLookupKey, TradeItemPriceSnapshot>> GetLatestItemPricesAsync(
        IReadOnlyCollection<TradePriceLookupKey> keys,
        DateOnly asOfDate,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<TradeWarehouseItemKey, decimal>> GetLatestUnitCostsAsync(
        IReadOnlyCollection<TradeWarehouseItemKey> keys,
        DateOnly asOfDate,
        CancellationToken ct = default);
}
