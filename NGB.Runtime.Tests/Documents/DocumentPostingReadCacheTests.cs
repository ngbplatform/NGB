using FluentAssertions;
using NGB.Runtime.Documents;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentPostingReadCacheTests
{
    [Fact]
    public async Task Outside_scope_reads_are_never_cached()
    {
        var cache = new DocumentPostingReadCache();
        var calls = 0;

        await cache.GetOrAddAsync("document:1", _ => Task.FromResult(++calls));
        await cache.GetOrAddAsync("document:1", _ => Task.FromResult(++calls));

        calls.Should().Be(2);
    }

    [Fact]
    public async Task Nested_scopes_share_values_and_outer_scope_disposal_discards_them()
    {
        var cache = new DocumentPostingReadCache();
        var calls = 0;

        using (cache.BeginScope())
        {
            (await cache.GetOrAddAsync("document:1", _ => Task.FromResult(++calls))).Should().Be(1);
            using (cache.BeginScope())
            {
                (await cache.GetOrAddAsync("document:1", _ => Task.FromResult(++calls))).Should().Be(1);
            }

            (await cache.GetOrAddAsync("document:1", _ => Task.FromResult(++calls))).Should().Be(1);
        }

        using (cache.BeginScope())
        {
            (await cache.GetOrAddAsync("document:1", _ => Task.FromResult(++calls))).Should().Be(2);
        }
    }

    [Fact]
    public async Task Null_values_are_cached_with_the_declared_type()
    {
        var cache = new DocumentPostingReadCache();
        var calls = 0;
        using var scope = cache.BeginScope();

        (await cache.GetOrAddAsync<string?>("nullable", _ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        })).Should().BeNull();
        (await cache.GetOrAddAsync<string?>("nullable", _ => Task.FromResult<string?>("unexpected")))
            .Should().BeNull();

        calls.Should().Be(1);
    }

    [Fact]
    public async Task Failed_reads_are_not_cached()
    {
        var cache = new DocumentPostingReadCache();
        var calls = 0;
        using var scope = cache.BeginScope();

        var failedRead = () => cache.GetOrAddAsync<int>("document:1", _ =>
        {
            calls++;
            return Task.FromException<int>(new InvalidOperationException("failed"));
        });

        await failedRead.Should().ThrowAsync<InvalidOperationException>();
        (await cache.GetOrAddAsync("document:1", _ => Task.FromResult(++calls))).Should().Be(2);
    }

    [Fact]
    public async Task Reusing_a_key_with_another_type_is_rejected()
    {
        var cache = new DocumentPostingReadCache();
        using var scope = cache.BeginScope();
        await cache.GetOrAddAsync("shared", _ => Task.FromResult(42));

        var read = () => cache.GetOrAddAsync("shared", _ => Task.FromResult("forty-two"));

        await read.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*different value type*");
    }

    [Fact]
    public async Task Primed_values_are_returned_without_factory_calls_and_are_scope_local()
    {
        var cache = new DocumentPostingReadCache();
        cache.Prime("document:1", 10);

        using (cache.BeginScope())
        {
            cache.Prime("document:1", 42);
            cache.Prime("document:1", 99);
            (await cache.GetOrAddAsync("document:1", _ => Task.FromResult(-1))).Should().Be(42);

            var wrongType = () => cache.Prime("document:1", "forty-two");
            wrongType.Should().Throw<NgbInvariantViolationException>()
                .WithMessage("*different value type*");
        }

        using (cache.BeginScope())
        {
            (await cache.GetOrAddAsync("document:1", _ => Task.FromResult(7))).Should().Be(7);
        }
    }

    [Fact]
    public async Task Invalid_arguments_are_rejected_and_scope_disposal_is_idempotent()
    {
        var cache = new DocumentPostingReadCache();
        var scope = cache.BeginScope();

        var missingKey = () => cache.GetOrAddAsync(" ", _ => Task.FromResult(1));
        var missingFactory = () => cache.GetOrAddAsync<int>("key", null!);

        await missingKey.Should().ThrowAsync<NgbArgumentRequiredException>();
        await missingFactory.Should().ThrowAsync<ArgumentNullException>();
        scope.Dispose();
        scope.Dispose();
    }
}
