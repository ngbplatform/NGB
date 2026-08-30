namespace NGB.PropertyManagement.Reporting;

public interface IMaintenanceQueueReader
{
    Task<MaintenanceQueuePage> GetPageAsync(MaintenanceQueueQuery query, CancellationToken ct = default);

    async Task<MaintenanceQueuePage> GetCursorPageAsync(
        MaintenanceQueueQuery query,
        MaintenanceQueuePageCursor? cursor,
        CancellationToken ct = default)
    {
        var offset = cursor?.Offset ?? 0;
        var page = await GetPageAsync(query with { Offset = offset }, ct);
        return page with { HasMore = offset + page.Rows.Count < page.Total };
    }
}
