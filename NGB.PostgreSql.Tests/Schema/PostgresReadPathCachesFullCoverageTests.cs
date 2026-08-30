using FluentAssertions;
using NGB.OperationalRegisters.Contracts;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.PostgreSql.Schema;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
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

    [Fact]
    public async Task Relation_shape_cache_coalesces_positive_probes_and_separates_fingerprints()
    {
        var time = new AdjustableTimeProvider(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
        var cache = new PostgresRelationShapeCache(time);
        var calls = 0;

        (await cache.IsVerifiedAsync(
            "opreg_sales__movements",
            "movement_id|amount",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(true);
            },
            default)).Should().BeTrue();
        (await cache.IsVerifiedAsync(
            "opreg_sales__movements",
            "movement_id|amount",
            _ => throw new Xunit.Sdk.XunitException("Verified shape must be cached."),
            default)).Should().BeTrue();

        (await cache.IsVerifiedAsync(
            "opreg_sales__movements",
            "movement_id|amount|tax",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(true);
            },
            default)).Should().BeTrue();
        calls.Should().Be(2);

        var negativeCalls = 0;
        for (var i = 0; i < 2; i++)
        {
            (await cache.IsVerifiedAsync(
                "refreg_prices__records",
                "record_id|value",
                _ =>
                {
                    Interlocked.Increment(ref negativeCalls);
                    return Task.FromResult(false);
                },
                default)).Should().BeFalse();
        }
        negativeCalls.Should().Be(2);

        cache.MarkVerified("refreg_prices__records", "record_id|value");
        (await cache.IsVerifiedAsync(
            "refreg_prices__records",
            "record_id|value",
            _ => throw new Xunit.Sdk.XunitException("Explicitly verified shape must be cached."),
            default)).Should().BeTrue();

        cache.Invalidate("opreg_sales__movements");
        (await cache.IsVerifiedAsync(
            "opreg_sales__movements",
            "movement_id|amount",
            _ => Task.FromResult(true),
            default)).Should().BeTrue();

        time.Advance(TimeSpan.FromMinutes(6));
        (await cache.IsVerifiedAsync(
            "refreg_prices__records",
            "record_id|value",
            _ => Task.FromResult(true),
            default)).Should().BeTrue();
    }

    [Fact]
    public async Task Operational_metadata_cache_only_reuses_immutable_metadata()
    {
        var time = new AdjustableTimeProvider(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
        var cache = new OperationalRegisterMetadataCache(time);
        var immutableId = Guid.NewGuid();
        var immutable = OperationalContext(immutableId, hasMovements: true);
        var immutableCalls = 0;

        for (var i = 0; i < 2; i++)
        {
            (await cache.GetOrCreateAsync(
                immutableId,
                _ =>
                {
                    Interlocked.Increment(ref immutableCalls);
                    return Task.FromResult(immutable);
                },
                default)).Should().BeSameAs(immutable);
        }
        immutableCalls.Should().Be(1);

        var mutableId = Guid.NewGuid();
        var mutable = OperationalContext(mutableId, hasMovements: false);
        var mutableCalls = 0;
        for (var i = 0; i < 2; i++)
        {
            await cache.GetOrCreateAsync(
                mutableId,
                _ =>
                {
                    Interlocked.Increment(ref mutableCalls);
                    return Task.FromResult(mutable);
                },
                default);
        }
        mutableCalls.Should().Be(2);

        cache.Invalidate(immutableId);
        await cache.GetOrCreateAsync(immutableId, _ => Task.FromResult(immutable), default);
        time.Advance(TimeSpan.FromMinutes(6));
        await cache.GetOrCreateAsync(immutableId, _ => Task.FromResult(immutable), default);
    }

    [Fact]
    public async Task Reference_metadata_cache_only_reuses_immutable_metadata()
    {
        var time = new AdjustableTimeProvider(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
        var cache = new ReferenceRegisterMetadataCache(time);
        var immutableId = Guid.NewGuid();
        var immutable = ReferenceContext(immutableId, hasRecords: true);
        var immutableCalls = 0;

        for (var i = 0; i < 2; i++)
        {
            (await cache.GetOrCreateAsync(
                immutableId,
                _ =>
                {
                    Interlocked.Increment(ref immutableCalls);
                    return Task.FromResult(immutable);
                },
                default)).Should().BeSameAs(immutable);
        }
        immutableCalls.Should().Be(1);

        var mutableId = Guid.NewGuid();
        var mutable = ReferenceContext(mutableId, hasRecords: false);
        var mutableCalls = 0;
        cache.Remember(mutable);
        for (var i = 0; i < 2; i++)
        {
            await cache.GetOrCreateAsync(
                mutableId,
                _ =>
                {
                    Interlocked.Increment(ref mutableCalls);
                    return Task.FromResult(mutable);
                },
                default);
        }
        mutableCalls.Should().Be(2);

        cache.Invalidate(immutableId);
        await cache.GetOrCreateAsync(immutableId, _ => Task.FromResult(immutable), default);
        time.Advance(TimeSpan.FromMinutes(6));
        await cache.GetOrCreateAsync(immutableId, _ => Task.FromResult(immutable), default);
    }

    private static OperationalRegisterMetadataContext OperationalContext(Guid registerId, bool hasMovements)
        => new(
            new OperationalRegisterAdminItem(
                registerId,
                "Sales",
                "sales",
                "sales",
                "Sales",
                hasMovements,
                DateTime.UnixEpoch,
                DateTime.UnixEpoch),
            [],
            "opreg_sales__movements");

    private static ReferenceRegisterMetadataContext ReferenceContext(Guid registerId, bool hasRecords)
        => new(
            new ReferenceRegisterAdminItem(
                registerId,
                "Prices",
                "prices",
                "prices",
                "Prices",
                ReferenceRegisterPeriodicity.NonPeriodic,
                ReferenceRegisterRecordMode.Independent,
                hasRecords,
                DateTime.UnixEpoch,
                DateTime.UnixEpoch),
            [],
            "refreg_prices__records");

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
