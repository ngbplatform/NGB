namespace NGB.PropertyManagement.Reporting;

public interface ITenantStatementReader
{
    Task<TenantStatementPage> GetPageAsync(TenantStatementQuery query, CancellationToken ct = default);

    async Task<TenantStatementPage> GetCursorPageAsync(
        TenantStatementQuery query,
        TenantStatementPageCursor? cursor,
        CancellationToken ct = default)
    {
        var offset = cursor?.Offset ?? 0;
        var page = await GetPageAsync(query with { Offset = offset }, ct);
        return page with { HasMore = offset + page.Rows.Count < page.Total };
    }
}
