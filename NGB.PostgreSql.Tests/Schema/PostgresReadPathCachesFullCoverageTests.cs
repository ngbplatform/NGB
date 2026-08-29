using FluentAssertions;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.Schema;
using Xunit;

namespace NGB.PostgreSql.Tests.Schema;

public sealed class PostgresReadPathCachesFullCoverageTests
{
    [Fact]
    public async Task Relation_presence_cache_coalesces_positive_probes_but_rechecks_missing_relations()
    {
        var time = new AdjustableTimeProvider(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
        var cache = new PostgresRelationPresenceCache(time);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var positiveCalls = 0;

        async Task<bool> PositiveProbe(CancellationToken _)
        {
            Interlocked.Increment(ref positiveCalls);
            started.TrySetResult();
            return await release.Task;
        }

        var first = cache.ExistsAsync("public.prices", PositiveProbe, default);
        await started.Task;
        var second = cache.ExistsAsync("public.prices", PositiveProbe, default);
        release.SetResult(true);

        (await first).Should().BeTrue();
        (await second).Should().BeTrue();
        (await cache.ExistsAsync(
            "public.prices",
            _ => throw new Xunit.Sdk.XunitException("A positive relation probe must be cached."),
            default)).Should().BeTrue();
        positiveCalls.Should().Be(1);

        time.Advance(TimeSpan.FromMinutes(6));
        var expiredCalls = 0;
        (await cache.ExistsAsync(
            "public.prices",
            _ =>
            {
                Interlocked.Increment(ref expiredCalls);
                return Task.FromResult(true);
            },
            default)).Should().BeTrue();
        expiredCalls.Should().Be(1);

        var missingCalls = 0;
        for (var i = 0; i < 2; i++)
        {
            (await cache.ExistsAsync(
                "public.not_created_yet",
                _ =>
                {
                    Interlocked.Increment(ref missingCalls);
                    return Task.FromResult(false);
                },
                default)).Should().BeFalse();
        }
        missingCalls.Should().Be(2);

        cache.Invalidate("public.prices");
        (await cache.ExistsAsync("public.prices", _ => Task.FromResult(true), default)).Should().BeTrue();
    }

    [Fact]
    public async Task Operational_register_context_cache_coalesces_expires_and_never_caches_missing_tables()
    {
        var time = new AdjustableTimeProvider(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
        var cache = new OperationalRegisterReadContextCache(time);
        var registerId = Guid.NewGuid();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<OperationalRegisterReadContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<OperationalRegisterReadContext> Load(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            return await release.Task;
        }

        var first = cache.GetOrCreateAsync(registerId, "amount", Load, default);
        await started.Task;
        var second = cache.GetOrCreateAsync(registerId, "amount", Load, default);
        var expected = new OperationalRegisterReadContext("movements", "balances", true, true);
        release.SetResult(expected);

        (await first).Should().BeSameAs(expected);
        (await second).Should().BeSameAs(expected);
        calls.Should().Be(1);

        time.Advance(TimeSpan.FromMinutes(6));
        var refreshed = new OperationalRegisterReadContext("movements-v2", "balances-v2", true, false);
        (await cache.GetOrCreateAsync(registerId, "amount", _ => Task.FromResult(refreshed), default))
            .Should().BeSameAs(refreshed);

        cache.Invalidate(registerId);
        var afterInvalidation = new OperationalRegisterReadContext("movements-v3", "balances-v3", true, true);
        (await cache.GetOrCreateAsync(registerId, "amount", _ => Task.FromResult(afterInvalidation), default))
            .Should().BeSameAs(afterInvalidation);

        var missingCalls = 0;
        var missingRegisterId = Guid.NewGuid();
        for (var i = 0; i < 2; i++)
        {
            var missing = await cache.GetOrCreateAsync(
                missingRegisterId,
                "qty_delta",
                _ =>
                {
                    Interlocked.Increment(ref missingCalls);
                    return Task.FromResult(new OperationalRegisterReadContext("missing", "missing", false, false));
                },
                default);
            missing.MovementsExist.Should().BeFalse();
        }
        missingCalls.Should().Be(2);
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
