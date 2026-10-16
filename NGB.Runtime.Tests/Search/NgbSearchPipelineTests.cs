using Microsoft.Extensions.Logging.Abstractions;
using NGB.Runtime.Search;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Search;

public sealed class NgbSearchPipelineTests
{
    [Theory]
    [InlineData(":", "documents")]
    [InlineData("document", "documents")]
    [InlineData("documents", "documents")]
    [InlineData("@", "catalogs")]
    [InlineData("catalog", "catalogs")]
    [InlineData("catalogs", "catalogs")]
    [InlineData("#", "reports")]
    [InlineData("report", "reports")]
    [InlineData(" REPORTS ", "reports")]
    [InlineData("/", null)]
    [InlineData("page", null)]
    [InlineData("pages", null)]
    [InlineData(">", null)]
    [InlineData("command", null)]
    [InlineData("commands", null)]
    [InlineData("unknown", null)]
    public async Task ScopeAliasesInvokeOnlyTheirMatchingReadSource(string scope, string? expected)
    {
        var calls = new List<string>();
        Task<string?> Read(string code, string query, int limit)
        {
            Assert.Equal("alpha", query);
            Assert.Equal(6, limit);
            calls.Add(code);
            return Task.FromResult<string?>(code);
        }
        var result = await NgbSearchPipeline.ExecuteAsync(" alpha ", 100, scope, 6,
            (query, limit, _) => Read("documents", query, limit),
            (query, limit, _) => Read("catalogs", query, limit),
            (query, limit, _) => Read("reports", query, limit), NullLogger.Instance, default);
        Assert.Equal(expected is null ? [] : new[] { expected }, calls);
        Assert.Equal(calls, result.Groups);
        Assert.Empty(result.UnavailableSources);
    }

    [Fact]
    public async Task EmptyAndOversizedQueriesAndInvalidLimitsDoNotInvokeSources()
    {
        Task<string?> Unexpected(string query, int limit, CancellationToken ct)
            => throw new InvalidOperationException("A rejected input must not query a source.");
        var empty = await NgbSearchPipeline.ExecuteAsync(" ", 0, null, 50, Unexpected, Unexpected, Unexpected, NullLogger.Instance, default);
        Assert.Empty(empty.Groups);
        await Assert.ThrowsAsync<NgbArgumentOutOfRangeException>(() => NgbSearchPipeline.ExecuteAsync(
            new string('x', 257), 20, null, 50, Unexpected, Unexpected, Unexpected, NullLogger.Instance, default));
        foreach (var maximum in new[] { 0, 51 })
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => NgbSearchPipeline.ExecuteAsync(
                "alpha", 20, null, maximum, Unexpected, Unexpected, Unexpected, NullLogger.Instance, default));
        }
    }

    [Fact]
    public async Task SourcesAreSequentialAndFailurePreservesExplicitPartialResults()
    {
        var order = new List<string>();
        var inFlight = false;
        async Task<string?> Read(string source, int limit)
        {
            Assert.Equal(20, limit);
            Assert.False(inFlight);
            inFlight = true;
            await Task.Yield();
            inFlight = false;
            order.Add(source);
            if (source == "catalogs")
                throw new IOException("private provider details");
            return source == "reports" ? null : source;
        }
        var result = await NgbSearchPipeline.ExecuteAsync("alpha", -1, "", 50,
            (_, limit, _) => Read("documents", limit), (_, limit, _) => Read("catalogs", limit),
            (_, limit, _) => Read("reports", limit), NullLogger.Instance, default);
        Assert.Equal(new[] { "documents", "catalogs", "reports" }, order);
        Assert.Equal(new[] { "documents" }, result.Groups);
        Assert.Equal(new[] { "catalogs" }, result.UnavailableSources);
    }

    [Fact]
    public async Task RequestCancellationStopsSubsequentSources()
    {
        using var cancellation = new CancellationTokenSource();
        Task<string?> Cancel(string query, int limit, CancellationToken ct)
        {
            cancellation.Cancel();
            return Task.FromCanceled<string?>(ct);
        }
        Task<string?> Unexpected(string query, int limit, CancellationToken ct)
            => throw new InvalidOperationException("Later sources must not run after cancellation.");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NgbSearchPipeline.ExecuteAsync(
            "alpha", 20, null, 50, Cancel, Unexpected, Unexpected, NullLogger.Instance, cancellation.Token));
    }
}
