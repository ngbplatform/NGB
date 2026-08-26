namespace NGB.Trade.Reporting;

public sealed record TradeCurrentItemPriceRow(
    Guid ItemId,
    string ItemDisplay,
    Guid PriceTypeId,
    string PriceTypeDisplay,
    string Currency,
    decimal UnitPrice,
    DateOnly? EffectiveDate,
    Guid? SourceDocumentId);

public sealed record TradeCurrentItemPricePage(IReadOnlyList<TradeCurrentItemPriceRow> Rows, int Total);

public interface ITradeCurrentItemPriceReader
{
    Task<TradeCurrentItemPricePage> GetPageAsync(
        DateTime asOfUtc,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? priceTypeIds,
        int offset,
        int limit,
        CancellationToken ct = default);
}
