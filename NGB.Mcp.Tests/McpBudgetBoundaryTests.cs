using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Security;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpBudgetBoundaryTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(McpReadOptions.MaximumSupportedJsonDepth)]
    public async Task ArgumentDepthCountsContainersAndRejectsExcessBeforeExecution(int maximumDepth)
    {
        var services = Services();
        services.Configure<McpReadOptions>(options => options.MaxJsonDepth = maximumDepth);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();
        using var accepted = NestedInput(maximumDepth);
        using var rejected = NestedInput(maximumDepth + 1);

        await dispatcher.ExecuteAsync("custom_limits", accepted.RootElement, default);
        var error = await Assert.ThrowsAsync<McpReadException>(() =>
            dispatcher.ExecuteAsync("custom_limits", rejected.RootElement, default));

        Assert.Equal("invalid_input", error.Code);
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<ExecutionCounter>().Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InputByteBudgetAcceptsTheExactUtf8SizeAndRejectsOneByteLess(bool belowSize)
    {
        using var input = JsonDocument.Parse("{\"value\":\"Привет\"}");
        var bytes = Encoding.UTF8.GetByteCount(input.RootElement.GetRawText());
        var services = Services();
        services.Configure<McpReadOptions>(options => options.MaxInputBytes = bytes - (belowSize ? 1 : 0));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();

        if (belowSize)
        {
            var error = await Assert.ThrowsAsync<McpReadException>(() => dispatcher.ExecuteAsync("custom_limits", input.RootElement, default));
            Assert.Equal("invalid_input", error.Code);
            Assert.Equal(0, scope.ServiceProvider.GetRequiredService<ExecutionCounter>().Count);
        }
        else
        {
            await dispatcher.ExecuteAsync("custom_limits", input.RootElement, default);
            Assert.Equal(1, scope.ServiceProvider.GetRequiredService<ExecutionCounter>().Count);
        }
    }

    private static ServiceCollection Services()
    {
        var services = McpTestServices.Create();
        services.AddNgbMcpContributor<Contributor>();
        services.AddScoped<Handler>();
        services.AddScoped<ExecutionCounter>();
        return services;
    }

    private static JsonDocument NestedInput(int depth)
        => JsonDocument.Parse("{\"value\":" + string.Concat(Enumerable.Repeat("{\"child\":", depth - 1))
            + "0" + new string('}', depth));

    public sealed record Request(JsonElement Value);
    public sealed record Result(bool Completed);

    public sealed class ExecutionCounter
    {
        public int Count { get; set; }
    }

    public sealed class Handler(ExecutionCounter counter) : IMcpReadToolHandler<Request, Result>
    {
        public Task<Result> ExecuteAsync(Request request, CancellationToken ct)
        {
            counter.Count++;
            return Task.FromResult(new Result(true));
        }
    }

    public sealed class Contributor : IMcpReadToolContributor
    {
        public void Contribute(IMcpReadToolRegistryBuilder builder)
            => builder.Add<Request, Result, Handler>("custom_limits", "Read a bounded value.", [NgbSystemPermissions.AuditView]);
    }
}
