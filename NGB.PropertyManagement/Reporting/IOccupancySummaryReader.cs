namespace NGB.PropertyManagement.Reporting;

public interface IOccupancySummaryReader
{
    Task<OccupancySummaryPage> GetPageAsync(
        Guid? buildingId,
        DateOnly asOfUtc,
        int offset,
        int limit,
        CancellationToken ct = default);

    async Task<OccupancySummaryPage> GetCursorPageAsync(
        Guid? buildingId,
        DateOnly asOfUtc,
        OccupancySummaryPageCursor? cursor,
        int limit,
        CancellationToken ct = default)
    {
        var offset = cursor?.Offset ?? 0;
        var page = await GetPageAsync(buildingId, asOfUtc, offset, limit, ct);
        return page with { HasMore = offset + page.Rows.Count < page.Total };
    }
}
