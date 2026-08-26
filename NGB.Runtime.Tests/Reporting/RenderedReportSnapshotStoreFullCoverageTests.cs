using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class RenderedReportSnapshotStoreFullCoverageTests
{
    [Fact]
    public async Task MemoryStore_SetGetRemove_RoundTripsSnapshot()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheRenderedReportSnapshotStore(cache);
        var snapshot = Snapshot();

        (await store.GetAsync(snapshot.SnapshotId, default)).Should().BeNull();
        (await store.SetAsync(snapshot, default)).Should().BeTrue();
        (await store.GetAsync(snapshot.SnapshotId, default)).Should().BeSameAs(snapshot);
        await store.RemoveAsync(snapshot.SnapshotId, default);
        (await store.GetAsync(snapshot.SnapshotId, default)).Should().BeNull();
    }

    [Fact]
    public async Task MemoryStore_CanceledOperations_ThrowBeforeTouchingCache()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheRenderedReportSnapshotStore(cache);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await ((Func<Task>)(() => store.GetAsync(Guid.CreateVersion7(), cancellation.Token)))
            .Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => store.SetAsync(Snapshot(), cancellation.Token)))
            .Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => store.RemoveAsync(Guid.CreateVersion7(), cancellation.Token)))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task BoundedMemoryStore_RejectsSingleSnapshotLargerThanItsGlobalRowBudget()
    {
        using var store = new MemoryCacheRenderedReportSnapshotStore();
        var snapshot = Snapshot() with
        {
            ContentRows = Enumerable.Repeat<ReportSheetRowDto>(null!, 50_001).ToArray(),
            TotalContentRows = 50_001
        };

        (await store.SetAsync(snapshot, default)).Should().BeFalse();
        (await store.GetAsync(snapshot.SnapshotId, default)).Should().BeNull();
    }

    [Fact]
    public async Task NullStore_AlwaysMissesRejectsSetAndAcceptsRemove()
    {
        var store = NullRenderedReportSnapshotStore.Instance;
        var snapshot = Snapshot();

        (await store.GetAsync(snapshot.SnapshotId, default)).Should().BeNull();
        (await store.SetAsync(snapshot, default)).Should().BeFalse();
        await store.RemoveAsync(snapshot.SnapshotId, default);
    }

    private static RenderedReportSnapshot Snapshot()
        => new(
            Guid.CreateVersion7(),
            "report",
            Guid.CreateVersion7(),
            null!,
            [],
            null,
            0);
}
