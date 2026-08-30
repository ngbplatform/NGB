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

public sealed record TradeCurrentItemPricePage(IReadOnlyList<TradeCurrentItemPriceRow> Rows, int Total, bool HasMore = false);

public sealed record TradeCurrentItemPricePageCursor(int Offset, int Total, DateTime AsOfUtc = default);

public interface ITradeCurrentItemPriceReader
{
    Task<TradeCurrentItemPricePage> GetPageAsync(
        DateTime asOfUtc,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? priceTypeIds,
        int offset,
        int limit,
        CancellationToken ct = default);

    async Task<TradeCurrentItemPricePage> GetCursorPageAsync(
        DateTime asOfUtc,
        IReadOnlyList<Guid>? itemIds,
        IReadOnlyList<Guid>? priceTypeIds,
        TradeCurrentItemPricePageCursor? cursor,
        int limit,
        CancellationToken ct = default)
    {
        var offset = cursor?.Offset ?? 0;
        var page = await GetPageAsync(asOfUtc, itemIds, priceTypeIds, offset, limit, ct);
        return page with { HasMore = offset + page.Rows.Count < page.Total };
    }
}
