using FluentAssertions;
using NGB.PostgreSql.Internal;
using Xunit;

namespace NGB.PostgreSql.Tests.Internal;

public sealed class AsyncCachePrimitivesTests
{
    [Fact]
    public async Task Keyed_lock_coalesces_waiters_handles_cancellation_and_retires_entries()
    {
        var keyedLock = new AsyncKeyedLock<string>(StringComparer.Ordinal);
        var first = await keyedLock.AcquireAsync("key", default);
        var waiting = keyedLock.AcquireAsync("key", default).AsTask();
        using var cancellation = new CancellationTokenSource();
        var cancelled = keyedLock.AcquireAsync("key", cancellation.Token).AsTask();
        cancellation.Cancel();

        await ((Func<Task>)(async () => await cancelled)).Should().ThrowAsync<OperationCanceledException>();
        keyedLock.Count.Should().Be(1);
        waiting.IsCompleted.Should().BeFalse();

        first.Dispose();
        var second = await waiting;
        second.Dispose();
        second.Dispose();

        keyedLock.Count.Should().Be(0);
    }

    [Fact]
    public void Bounded_cache_expires_invalidates_and_evicts_oldest_generation()
    {
        var now = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        Action invalid = () => _ = new BoundedExpiringCache<string, int>(0);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
        var cache = new BoundedExpiringCache<string, int>(2, StringComparer.OrdinalIgnoreCase);

        cache.Set("first", 1, now.AddMinutes(5), now);
        cache.Set("second", 2, now.AddMinutes(1), now);
        cache.TryGet("FIRST", now, out var first).Should().BeTrue();
        first.Should().Be(1);

        cache.Set("third", 3, now.AddMinutes(5), now);
        cache.Count.Should().Be(2);
        cache.TryGet("first", now, out _).Should().BeFalse();
        cache.TryGet("second", now.AddMinutes(2), out _).Should().BeFalse();

        cache.Set("fourth", 4, now.AddMinutes(5), now);
        cache.RemoveWhere(static key => key.StartsWith("th", StringComparison.Ordinal));
        cache.TryGet("third", now, out _).Should().BeFalse();
        cache.Remove("fourth");
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void Bounded_cache_purges_out_of_order_expirations_and_ignores_stale_replacement_tokens()
    {
        var now = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var cache = new BoundedExpiringCache<string, int>(3, StringComparer.OrdinalIgnoreCase);

        cache.Set("long", 1, now.AddMinutes(10), now);
        cache.Set("short", 2, now.AddMinutes(1), now);
        cache.Set("replaced", 3, now.AddMinutes(1), now);
        cache.Set("REPLACED", 4, now.AddMinutes(10), now);

        cache.Set("trigger", 5, now.AddMinutes(10), now.AddMinutes(2));

        cache.TryGet("short", now.AddMinutes(2), out _).Should().BeFalse();
        cache.TryGet("replaced", now.AddMinutes(2), out var replacement).Should().BeTrue();
        replacement.Should().Be(4);
        cache.Count.Should().Be(3);

        ((Action)(() => cache.RemoveWhere(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Bounded_cache_compacts_stale_expiration_metadata_without_changing_the_latest_value()
    {
        var now = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var cache = new BoundedExpiringCache<string, int>(2);

        for (var value = 0; value < 100; value++)
            cache.Set("same", value, now.AddMinutes(value + 1), now);

        cache.TryGet("same", now, out var latest).Should().BeTrue();
        latest.Should().Be(99);
        cache.Count.Should().Be(1);
    }
}
