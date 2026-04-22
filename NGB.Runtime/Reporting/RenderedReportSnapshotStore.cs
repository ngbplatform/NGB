using Microsoft.Extensions.Caching.Memory;
using NGB.Contracts.Reporting;

namespace NGB.Runtime.Reporting;

public interface IRenderedReportSnapshotStore
{
    Task<RenderedReportSnapshot?> GetAsync(Guid snapshotId, CancellationToken ct);
    Task<bool> SetAsync(RenderedReportSnapshot snapshot, CancellationToken ct);
    Task RemoveAsync(Guid snapshotId, CancellationToken ct);
}

public sealed record RenderedReportSnapshot(
    Guid SnapshotId,
    string ReportCode,
    Guid Fingerprint,
    ReportSheetDto TemplateSheet,
    IReadOnlyList<ReportSheetRowDto> ContentRows,
    ReportSheetRowDto? GrandTotalRow,
    int TotalContentRows,
    IReadOnlyDictionary<string, string>? Diagnostics = null);

public sealed class MemoryCacheRenderedReportSnapshotStore(IMemoryCache cache) : IRenderedReportSnapshotStore
{
    private static readonly TimeSpan SlidingTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AbsoluteTtl = TimeSpan.FromMinutes(10);

    public Task<RenderedReportSnapshot?> GetAsync(Guid snapshotId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        cache.TryGetValue(Key(snapshotId), out RenderedReportSnapshot? snapshot);
        return Task.FromResult(snapshot);
    }

    public Task<bool> SetAsync(RenderedReportSnapshot snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        cache.Set(
            Key(snapshot.SnapshotId),
            snapshot,
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = SlidingTtl,
                AbsoluteExpirationRelativeToNow = AbsoluteTtl
            });

        return Task.FromResult(true);
    }

    public Task RemoveAsync(Guid snapshotId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        cache.Remove(Key(snapshotId));
        return Task.CompletedTask;
    }

    private static string Key(Guid snapshotId) => $"report:snapshot:{snapshotId:D}";
}

public sealed class NullRenderedReportSnapshotStore : IRenderedReportSnapshotStore
{
    public static readonly NullRenderedReportSnapshotStore Instance = new();

    private NullRenderedReportSnapshotStore()
    {
    }

    public Task<RenderedReportSnapshot?> GetAsync(Guid snapshotId, CancellationToken ct)
        => Task.FromResult<RenderedReportSnapshot?>(null);

    public Task<bool> SetAsync(RenderedReportSnapshot snapshot, CancellationToken ct)
        => Task.FromResult(false);

    public Task RemoveAsync(Guid snapshotId, CancellationToken ct)
        => Task.CompletedTask;
}
