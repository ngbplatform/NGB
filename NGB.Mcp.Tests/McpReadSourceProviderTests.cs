using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Security;
using NGB.Mcp.Tools;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpReadSourceProviderTests
{
    [Fact]
    public async Task SourcesUseTheSameScopedHandlerAndSuccessfulResult()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SourceHandler>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();

        var result = await dispatcher.ExecuteAsync("custom_sources", JsonSerializer.SerializeToElement(new { }), default);

        var source = Assert.Single(result.GetProperty("sources").EnumerateArray());
        Assert.Equal(result.GetProperty("data").GetProperty("id").GetGuid(), source.GetProperty("id").GetGuid());
        Assert.Equal("document", source.GetProperty("kind").GetString());
        Assert.Equal("Payment", source.GetProperty("title").GetString());
        Assert.Equal(1, handler.SourceCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeniedReadsNeverResolveSources(bool objectDenied)
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SourceHandler>();
        handler.DenyRead = objectDenied;
        if (!objectDenied)
            scope.ServiceProvider.GetRequiredService<McpTestServices.TestActor>().Snapshot = McpTestServices.Snapshot(false, []);

        var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();
        await Assert.ThrowsAsync<McpReadException>(() => dispatcher.ExecuteAsync(
            "custom_sources", JsonSerializer.SerializeToElement(new { }), default));

        Assert.Equal(0, handler.SourceCalls);
    }

    [Fact]
    public async Task SourcesShareTheResponseByteBudget()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<SourceHandler>().Title = new string('x', 4096);
        var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();

        var error = await Assert.ThrowsAsync<McpReadException>(() => dispatcher.ExecuteAsync(
            "custom_sources", JsonSerializer.SerializeToElement(new { }), default));

        Assert.Equal("result_too_large", error.Code);
    }

    [Fact]
    public async Task CancellationReachesSourceResolution()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SourceHandler>();
        handler.WaitForCancellation = true;
        var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();
        using var cancellation = new CancellationTokenSource();

        var execution = dispatcher.ExecuteAsync("custom_sources", JsonSerializer.SerializeToElement(new { }), cancellation.Token);
        await handler.Resolving.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.Equal(1, handler.SourceCalls);
    }

    private static ServiceCollection Services()
    {
        var services = McpTestServices.Create();
        services.Configure<McpReadOptions>(options => options.MaxResponseBytes = 4096);
        services.AddNgbMcpContributor<SourceContributor>();
        services.AddScoped<SourceHandler>();
        return services;
    }

    public sealed record SourceRequest;
    public sealed record SourceResult(Guid Id);

    public sealed class SourceHandler
        : IMcpReadToolHandler<SourceRequest, SourceResult>, IMcpReadSourceProvider<SourceRequest, SourceResult>
    {
        public bool DenyRead { get; set; }
        public bool WaitForCancellation { get; set; }
        public string Title { get; set; } = "Payment";
        public int SourceCalls { get; private set; }
        public TaskCompletionSource Resolving { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<SourceResult> ExecuteAsync(SourceRequest request, CancellationToken ct)
        {
            if (DenyRead)
                throw new McpReadException("unavailable", "The record is unavailable.");

            return Task.FromResult(new SourceResult(Guid.NewGuid()));
        }

        public async ValueTask<IReadOnlyList<McpReadSource>> GetSourcesAsync(
            SourceRequest request,
            SourceResult result,
            CancellationToken ct)
        {
            SourceCalls++;
            Resolving.TrySetResult();
            if (WaitForCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);

            return [new(McpSourceKind.Document, "test.payment", result.Id, Title)];
        }
    }

    public sealed class SourceContributor : IMcpReadToolContributor
    {
        public void Contribute(IMcpReadToolRegistryBuilder builder)
            => builder.Add<SourceRequest, SourceResult, SourceHandler>(
                "custom_sources", "Read an authorized payment.", [NgbSystemPermissions.AuditView]);
    }
}
