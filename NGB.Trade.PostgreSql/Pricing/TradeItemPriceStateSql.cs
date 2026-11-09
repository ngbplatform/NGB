namespace NGB.Trade.PostgreSql.Pricing;

internal static class TradeItemPriceStateSql
{
    // Select each source's final state before filtering deletes or effective dates. Otherwise
    // cancelling or moving a price can resurrect an obsolete version of that same document.
    internal static string LatestBySource(string table, string dimensionSet, string? recordedAsOf)
    {
        var recordedFilter = recordedAsOf is null
            ? string.Empty
            : $"AND price.recorded_at_utc <= {recordedAsOf}";

        return $"""
            SELECT DISTINCT ON (price.source_document_id)
                price.unit_price,
                price.currency,
                price.effective_date,
                price.source_document_id,
                price.is_deleted,
                price.recorded_at_utc,
                price.record_id
            FROM {table} price
            WHERE price.dimension_set_id = {dimensionSet}
              AND price.recorder_document_id IS NULL
              {recordedFilter}
            ORDER BY price.source_document_id, price.recorded_at_utc DESC, price.record_id DESC
            """;
    }
}
