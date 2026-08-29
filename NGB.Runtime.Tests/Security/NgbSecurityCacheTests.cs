using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NGB.Core.Security;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Security;

public sealed class NgbSecurityCacheTests
{
    [Fact]
    public async Task GetOrCreateReportDefinitionsAsync_CachesByAccessVersion()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new NgbSecurityCache(
            memoryCache,
            new TestOptionsMonitor<NgbSecurityCacheOptions>(new NgbSecurityCacheOptions()));
        var userId = Guid.NewGuid();
        var firstSnapshot = CreateSnapshot(userId, accessVersion: 1);
        var secondSnapshot = CreateSnapshot(userId, accessVersion: 2);
        var calls = 0;

        var first = await cache.GetOrCreateReportDefinitionsAsync(
            firstSnapshot,
            _ => Task.FromResult(++calls),
            CancellationToken.None);
        var cached = await cache.GetOrCreateReportDefinitionsAsync(
            firstSnapshot,
            _ => Task.FromResult(++calls),
            CancellationToken.None);
        var afterAccessChange = await cache.GetOrCreateReportDefinitionsAsync(
            secondSnapshot,
            _ => Task.FromResult(++calls),
            CancellationToken.None);

        first.Should().Be(1);
        cached.Should().Be(1);
        afterAccessChange.Should().Be(2);
        calls.Should().Be(2);
    }

    [Fact]
    public void Validate_RejectsUnsafeTtls()
    {
        var validator = new NgbSecurityCacheOptionsValidator();

        var result = validator.Validate(
            Options.DefaultName,
            new NgbSecurityCacheOptions { ReportDefinitionsTtl = TimeSpan.Zero });

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(x => x.Contains(nameof(NgbSecurityCacheOptions.ReportDefinitionsTtl), StringComparison.Ordinal));

        validator.Validate(
                Options.DefaultName,
                new NgbSecurityCacheOptions { MaxEntries = 99 })
            .Failed.Should().BeTrue();
        validator.Validate(
                Options.DefaultName,
                new NgbSecurityCacheOptions { MaxEntries = 200_001 })
            .Failed.Should().BeTrue();
    }

    [Fact]
    public async Task Cache_EvictsOldestTrackedSecurityKeyAtConfiguredBound()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new NgbSecurityCache(
            memoryCache,
            new TestOptionsMonitor<NgbSecurityCacheOptions>(new NgbSecurityCacheOptions { MaxEntries = 100 }));
        var firstUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        for (var index = 0; index <= 100; index++)
        {
            var userId = index == 0
                ? firstUserId
                : Guid.Parse($"00000000-0000-0000-0000-{index + 1:000000000000}");
            await cache.GetOrCreatePermissionSnapshotAsync(
                userId,
                accessVersion: 1,
                _ => Task.FromResult(index),
                CancellationToken.None);
        }

        var reloaded = await cache.GetOrCreatePermissionSnapshotAsync(
            firstUserId,
            accessVersion: 1,
            _ => Task.FromResult(999),
            CancellationToken.None);

        reloaded.Should().Be(999);
    }

    [Fact]
    public async Task Concurrent_cold_reads_share_one_population_and_reuse_the_cached_value()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new NgbSecurityCache(
            memoryCache,
            new TestOptionsMonitor<NgbSecurityCacheOptions>(new NgbSecurityCacheOptions()));
        var snapshot = CreateSnapshot(Guid.NewGuid(), accessVersion: 1);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<int> Load(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await release.Task.WaitAsync(ct);
            return 42;
        }

        var reads = Enumerable.Range(0, 24)
            .Select(_ => cache.GetOrCreateMainMenuAsync(snapshot, Load, CancellationToken.None))
            .ToArray();
        await Task.Yield();
        release.SetResult();

        var values = await Task.WhenAll(reads);
        var cached = await cache.GetOrCreateMainMenuAsync(
            snapshot,
            _ => Task.FromResult(99),
            CancellationToken.None);

        values.Should().OnlyContain(value => value == 42);
        cached.Should().Be(42);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Failed_population_is_not_cached_and_a_later_request_can_retry()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new NgbSecurityCache(
            memoryCache,
            new TestOptionsMonitor<NgbSecurityCacheOptions>(new NgbSecurityCacheOptions()));
        var snapshot = CreateSnapshot(Guid.NewGuid(), accessVersion: 1);
        var calls = 0;

        Func<Task> failed = async () => await cache.GetOrCreateCatalogMetadataAsync<int>(
            snapshot,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromException<int>(new InvalidOperationException("failed"));
            },
            CancellationToken.None);

        await failed.Should().ThrowAsync<InvalidOperationException>();
        var retried = await cache.GetOrCreateCatalogMetadataAsync(
            snapshot,
            _ => Task.FromResult(17),
            CancellationToken.None);

        retried.Should().Be(17);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Cancelling_one_waiter_does_not_cancel_a_shared_population_for_healthy_waiters()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new NgbSecurityCache(
            memoryCache,
            new TestOptionsMonitor<NgbSecurityCacheOptions>(new NgbSecurityCacheOptions()));
        var snapshot = CreateSnapshot(Guid.NewGuid(), accessVersion: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<int> Load(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            started.SetResult();
            await release.Task.WaitAsync(ct);
            return 42;
        }

        using var cancelledWaiter = new CancellationTokenSource();
        var first = cache.GetOrCreateMainMenuAsync(snapshot, Load, cancelledWaiter.Token);
        await started.Task;
        var healthy = cache.GetOrCreateMainMenuAsync(snapshot, Load, CancellationToken.None);

        cancelledWaiter.Cancel();
        await ((Func<Task>)(async () => await first)).Should().ThrowAsync<OperationCanceledException>();
        release.SetResult();

        (await healthy).Should().Be(42);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Cancelling_the_only_waiter_abandons_population_and_allows_retry()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new NgbSecurityCache(
            memoryCache,
            new TestOptionsMonitor<NgbSecurityCacheOptions>(new NgbSecurityCacheOptions()));
        var snapshot = CreateSnapshot(Guid.NewGuid(), accessVersion: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var cancellation = new CancellationTokenSource();
        var cancelled = cache.GetOrCreateCatalogMetadataAsync<int>(
            snapshot,
            async ct =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return 1;
            },
            cancellation.Token);

        await started.Task;
        cancellation.Cancel();
        await ((Func<Task>)(async () => await cancelled)).Should().ThrowAsync<OperationCanceledException>();

        var retried = await cache.GetOrCreateCatalogMetadataAsync(
            snapshot,
            _ => Task.FromResult(17),
            CancellationToken.None);

        retried.Should().Be(17);
    }

    private static PermissionSnapshot CreateSnapshot(Guid userId, long accessVersion)
        => new(
            userId,
            authSubject: $"subject-{userId:N}",
            isAuthenticated: true,
            isActive: true,
            isBootstrapAdmin: false,
            accessVersion: accessVersion,
            permissions: [new NgbPermissionKey(NgbResourceKinds.Report, "accounting.balance_sheet", NgbPermissionActions.View)]);

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
