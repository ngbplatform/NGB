using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Hosting.AspNetCore;
using NGB.Mcp.AspNetCore;
using NGB.Runtime.Security;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpTransportBoundaryTests
{
    [Fact]
    public async Task AlreadyBufferedChunkedRequestCannotBypassTheByteLimit()
    {
        using var request = new MemoryStream(Encoding.UTF8.GetBytes("{\"value\":\"" + new string('x', 1024) + "\"}"));
        var (http, filter, budget, provider) = Create(request);
        using (budget)
        using (provider)
        {
            Assert.Null(http.Request.ContentLength);
            var result = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), Unexpected);
            Assert.Equal(413, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            Assert.Same(request, http.Request.Body);
            Assert.True(request.CanRead);
        }
    }

    [Fact]
    public async Task BoundedBodyReplaysExactlyOnceAndRestoresTheHostStream()
    {
        const string json = "{\"params\":[{\"one\":1},{\"two\":2}]}";
        using var request = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var (http, filter, budget, provider) = Create(request);
        using (budget)
        using (provider)
        {
            var result = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), async context =>
            {
                using var reader = new StreamReader(context.HttpContext.Request.Body, leaveOpen: true);
                Assert.Equal(json, await reader.ReadToEndAsync());
                return Results.Ok();
            });
            Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            Assert.Same(request, http.Request.Body);
            Assert.True(request.CanRead);
        }
    }

    [Theory]
    [InlineData(32_768, 32_768, 200)]
    [InlineData(32_769, 32_768, 413)]
    [InlineData(2, int.MaxValue, 200)]
    public async Task ShortChunkReadsHonorTheExactBudgetWithoutAllocatingTheConfiguredMaximum(
        int bodyBytes, int maximumBytes, int expectedStatus)
    {
        var json = "{}" + new string(' ', bodyBytes - 2);
        using var request = new ChunkedStream(Encoding.UTF8.GetBytes(json));
        var (http, filter, budget, provider) = Create(request, maximumBytes: maximumBytes);
        using (budget)
        using (provider)
        {
            var result = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), async context =>
            {
                Assert.Equal(200, expectedStatus);
                using var reader = new StreamReader(context.HttpContext.Request.Body, leaveOpen: true);
                Assert.Equal(json, await reader.ReadToEndAsync());
                return Results.Ok();
            });

            Assert.Equal(expectedStatus, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            Assert.InRange(request.LargestReadBuffer, 1, 64 * 1024);
            Assert.True(request.Position <= (long)maximumBytes + 1);
            Assert.Same(request, http.Request.Body);
            using var lease = budget.Concurrency.AttemptAcquire();
            Assert.True(lease.IsAcquired);
        }
    }

    [Fact]
    public async Task DeadlineBeforeProtocolResponseReturnsGatewayTimeoutAndRestoresCancellation()
    {
        using var request = new DelayedStream();
        var (http, filter, budget, provider) = Create(request);
        using (budget)
        using (provider)
        using (var original = new CancellationTokenSource())
        {
            http.RequestAborted = original.Token;
            var result = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), Unexpected);
            Assert.Equal(504, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            Assert.Equal(original.Token, http.RequestAborted);
            Assert.False(original.IsCancellationRequested);
            using var lease = budget.Concurrency.AttemptAcquire();
            Assert.True(lease.IsAcquired);
        }
    }

    [Fact]
    public async Task BodyReadFailureDoesNotReachProtocolOrRetainAdmission()
    {
        using var request = new FailedStream();
        var (http, filter, budget, provider) = Create(request);
        using (budget)
        using (provider)
        {
            var result = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), Unexpected);
            Assert.Equal(413, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            using var lease = budget.Concurrency.AttemptAcquire();
            Assert.True(lease.IsAcquired);
        }
    }

    [Fact]
    public async Task PlainHttpToProductionResourceIsRejectedBeforeReadingBody()
    {
        using var request = new FailedStream();
        var (http, filter, budget, provider) = Create(request);
        using (budget)
        using (provider)
        {
            http.Request.Scheme = "http";
            var result = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), Unexpected);
            Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        }
    }

    [Theory]
    [InlineData("", "/other")]
    [InlineData("/wrong-prefix", "/mcp")]
    public async Task NonCanonicalRequestPathIsRejectedBeforeReadingBody(string pathBase, string path)
    {
        using var request = new FailedStream();
        var (http, filter, budget, provider) = Create(request);
        using (budget)
        using (provider)
        {
            http.Request.PathBase = pathBase;
            http.Request.Path = path;
            var result = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), Unexpected);
            Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        }
    }

    [Fact]
    public async Task AnonymousSnapshotIsRejectedEvenIfTheTransportWasAuthenticated()
    {
        using var request = new FailedStream();
        var (http, filter, budget, provider) = Create(request, PermissionSnapshot.Anonymous);
        using (budget)
        using (provider)
        {
            var result = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), Unexpected);
            Assert.Equal(403, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        }
    }

    private static (DefaultHttpContext Http, McpEndpointFilter Filter, McpRequestBudget Budget, ServiceProvider Provider)
        Create(Stream request, PermissionSnapshot? snapshot = null, int maximumBytes = 1024)
    {
        var settings = Options.Create(new NgbMcpOptions
        {
            Resource = "https://ngb.example/mcp",
            MaxRequestBytes = maximumBytes,
            ConcurrentRequests = 1,
            RequestTimeoutSeconds = 1
        });
        var access = new Mock<INgbAccessChecker>();
        access.Setup(value => value.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot ?? McpTestServices.Snapshot(true));
        var provider = new ServiceCollection().AddSingleton(access.Object).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("ngb.example");
        http.Request.Method = "POST";
        http.Request.Path = "/mcp";
        http.Request.Body = request;
        var budget = new McpRequestBudget(settings);
        var endpoint = new McpEndpointAddress(settings.Value, new NgbHostingOptions());
        endpoint.Map("/mcp");
        return (http, new McpEndpointFilter(settings, budget, endpoint), budget, provider);
    }

    private static ValueTask<object?> Unexpected(EndpointFilterInvocationContext context)
        => throw new InvalidOperationException("The rejected request must not reach the protocol handler.");

    private sealed class DelayedStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    private sealed class FailedStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(new IOException("fixture body read failure"));
    }

    private sealed class ChunkedStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal int LargestReadBuffer { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            LargestReadBuffer = Math.Max(LargestReadBuffer, buffer.Length);
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 37)], cancellationToken);
        }
    }
}
