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

public sealed class MemoryCacheRenderedReportSnapshotStore : IRenderedReportSnapshotStore, IDisposable
{
    internal const long MaxCachedRenderedRows = 50_000;

    private static readonly TimeSpan SlidingTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AbsoluteTtl = TimeSpan.FromMinutes(10);

    private readonly IMemoryCache _cache;
    private readonly IDisposable? _ownedCache;

    public MemoryCacheRenderedReportSnapshotStore()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = MaxCachedRenderedRows
        });
        _cache = memoryCache;
        _ownedCache = memoryCache;
    }

    public MemoryCacheRenderedReportSnapshotStore(IMemoryCache cache)
        => _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    public Task<RenderedReportSnapshot?> GetAsync(Guid snapshotId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _cache.TryGetValue(Key(snapshotId), out RenderedReportSnapshot? snapshot);
        return Task.FromResult(snapshot);
    }

    public Task<bool> SetAsync(RenderedReportSnapshot snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var size = EstimateRetainedCellUnits(snapshot);
        if (size > MaxCachedRenderedRows)
            return Task.FromResult(false);

        _cache.Set(
            Key(snapshot.SnapshotId),
            snapshot,
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = SlidingTtl,
                AbsoluteExpirationRelativeToNow = AbsoluteTtl,
                Size = size
            });

        return Task.FromResult(true);
    }

    public Task RemoveAsync(Guid snapshotId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _cache.Remove(Key(snapshotId));
        return Task.CompletedTask;
    }

    private static string Key(Guid snapshotId) => $"report:snapshot:{snapshotId:D}";

    private static long EstimateRetainedCellUnits(RenderedReportSnapshot snapshot)
    {
        long size = 0;
        foreach (var row in snapshot.ContentRows)
        {
            size = checked(size + Math.Max(1, row?.Cells.Count ?? 0));
        }

        if (snapshot.GrandTotalRow is { } grandTotal)
            size = checked(size + Math.Max(1, grandTotal.Cells.Count));

        if (snapshot.TemplateSheet is { } template)
        {
            size = checked(size + Math.Max(1, template.Columns.Count));
            if (template.HeaderRows is not null)
            {
                foreach (var header in template.HeaderRows)
                {
                    size = checked(size + Math.Max(1, header.Cells.Count));
                }
            }
        }

        return Math.Max(1, size);
    }

    public void Dispose() => _ownedCache?.Dispose();
}

public sealed class NullRenderedReportSnapshotStore : IRenderedReportSnapshotStore
{
    public static readonly NullRenderedReportSnapshotStore Instance = new();

    private NullRenderedReportSnapshotStore()
    {
    }

    public Task<RenderedReportSnapshot?> GetAsync(Guid snapshotId, CancellationToken ct)
        => Task.FromResult<RenderedReportSnapshot?>(null);

    public Task<bool> SetAsync(RenderedReportSnapshot snapshot, CancellationToken ct) => Task.FromResult(false);

    public Task RemoveAsync(Guid snapshotId, CancellationToken ct) => Task.CompletedTask;
}
