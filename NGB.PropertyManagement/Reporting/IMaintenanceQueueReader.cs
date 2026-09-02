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

    async Task<MaintenanceQueueDashboard> GetDashboardAsync(
        DateOnly asOfUtc,
        int itemLimit,
        CancellationToken ct = default)
    {
        var page = await GetPageAsync(
            new MaintenanceQueueQuery(asOfUtc, null, null, null, null, null, null, 0, itemLimit),
            ct);

        return new MaintenanceQueueDashboard(
            page.Total,
            page.Rows.Count(static row => row.QueueState == MaintenanceQueueState.Overdue),
            page.Rows.Count(static row => row.AgingDays <= 3),
            page.Rows.Count(static row => row.AgingDays is >= 4 and <= 7),
            page.Rows.Count(static row => row.AgingDays is >= 8 and <= 14),
            page.Rows.Count(static row => row.AgingDays >= 15),
            page.Rows);
    }
}
