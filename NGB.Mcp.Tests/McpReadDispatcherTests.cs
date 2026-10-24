using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Security;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpReadDispatcherTests
{
    [Fact]
    public async Task EmptyApplicationExposesReadToolsWithoutInventingBusinessTypes()
    {
        using var provider = McpTestServices.Create().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();
        var tools = await dispatcher.ListAsync(default);
        Assert.Equal(15, tools.Count);
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("register", StringComparison.Ordinal));
        Assert.Contains(tools, tool => tool.Name == "search");
        Assert.Contains(tools, tool => tool.Name == "fetch");
        Assert.All(tools, tool => Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString()));
        var result = await dispatcher.ExecuteAsync("ngb_list_metadata", JsonSerializer.SerializeToElement(new { }), default);
        Assert.Empty(result.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"limit\":101}")]
    [InlineData("{\"unexpected\":true}")]
    [InlineData("{\"limit\":1,\"limit\":2}")]
    [InlineData("{\"kind\":55}")]
    [InlineData("[]")]
    public async Task InvalidInputsAreRejectedBeforeQueries(string json)
    {
        using var provider = McpTestServices.Create().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();
        using var input = JsonDocument.Parse(json);
        var error = await Assert.ThrowsAsync<McpReadException>(() => dispatcher.ExecuteAsync("ngb_list_metadata", input.RootElement, default));
        Assert.Equal("invalid_input", error.Code);
    }

    [Fact]
    public async Task RevocationAndInactiveUsersAreCheckedOnEveryInvocation()
    {
        using var provider = McpTestServices.Create().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();
        await dispatcher.ListAsync(default);
        scope.ServiceProvider.GetRequiredService<McpTestServices.TestActor>().Snapshot = NGB.Runtime.Security.PermissionSnapshot.Anonymous;
        var error = await Assert.ThrowsAsync<McpReadException>(() => dispatcher.ExecuteAsync("ngb_get_capabilities", JsonSerializer.SerializeToElement(new { }), default));
        Assert.Equal("access_denied", error.Code);
    }

    [Fact]
    public async Task ConcurrentScopesKeepCustomToolPermissionsAndHandlersIsolated()
    {
        var services = McpTestServices.Create(false);
        services.AddNgbMcpContributor<CustomContributor>();
        services.AddScoped<CustomHandler>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var tasks = Enumerable.Range(0, 20).Select(async index =>
        {
            using var scope = provider.CreateScope();
            var allowed = index % 2 == 0;
            scope.ServiceProvider.GetRequiredService<McpTestServices.TestActor>().Snapshot = McpTestServices.Snapshot(false,
                allowed ? [NgbSystemPermissions.AuditView] : []);
            var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();
            var tools = await dispatcher.ListAsync(default);
            Assert.Equal(allowed, tools.Any(tool => tool.Name == "custom_read"));
            if (allowed)
            {
                var result = await dispatcher.ExecuteAsync("custom_read", JsonSerializer.SerializeToElement(new { }), default);
                return result.GetProperty("data").GetProperty("instance").GetGuid();
            }
            await Assert.ThrowsAsync<McpReadException>(() => dispatcher.ExecuteAsync("custom_read", JsonSerializer.SerializeToElement(new { }), default));
            return Guid.Empty;
        });
        var instances = (await Task.WhenAll(tasks)).Where(id => id != Guid.Empty).ToArray();
        Assert.Equal(10, instances.Distinct().Count());
    }

    public sealed record CustomRequest;
    public sealed record CustomResult(Guid Instance);

    public sealed class CustomHandler : IMcpReadToolHandler<CustomRequest, CustomResult>
    {
        private readonly Guid _instance = Guid.NewGuid();

        public Task<CustomResult> ExecuteAsync(CustomRequest request, CancellationToken ct)
            => Task.FromResult(new CustomResult(_instance));
    }

    public sealed class CustomContributor : IMcpReadToolContributor
    {
        public void Contribute(IMcpReadToolRegistryBuilder builder)
            => builder.Add<CustomRequest, CustomResult, CustomHandler>("custom_read", "Read a custom capability.", [NgbSystemPermissions.AuditView]);
    }
}
