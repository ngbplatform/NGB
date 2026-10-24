using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NGB.Core.Security;
using NGB.Runtime.Security;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpRegistryAndBudgetTests
{
    [Theory]
    [InlineData("ngb_custom", true)]
    [InlineData("search", true)]
    [InlineData("fetch", true)]
    [InlineData("custom_read", false)]
    [InlineData("Custom-Read", true)]
    [InlineData("x", true)]
    public void ReservedInvalidOrUnpermissionedToolsFailRegistration(string name, bool permissioned)
    {
        var contributor = new DelegateContributor(builder => builder.Add<BudgetRequest, BudgetResult, BudgetHandler>(
            name, "A custom read.", permissioned ? [NgbSystemPermissions.AuditView] : []));
        Assert.Throws<ArgumentException>(() => new McpReadToolRegistry([contributor], Options.Create(new McpReadOptions())));
    }

    [Fact]
    public void DuplicateAndUntypedRegistrationsFailBeforeServing()
    {
        var contributor = Contributor();
        Assert.Throws<InvalidOperationException>(() => new McpReadToolRegistry([contributor, contributor], Options.Create(new McpReadOptions())));
        var untyped = new DelegateContributor(builder => builder.Add<object, BudgetResult, UntypedHandler>(
            "custom_untyped", "Untyped input is not a supported contract.", [NgbSystemPermissions.AuditView]));
        Assert.Throws<ArgumentException>(() => new McpReadToolRegistry([untyped], Options.Create(new McpReadOptions())));
        var scalar = new DelegateContributor(builder => builder.Add<string, BudgetResult, ScalarHandler>(
            "custom_scalar", "Scalar input is not an object contract.", [NgbSystemPermissions.AuditView]));
        Assert.Throws<ArgumentException>(() => new McpReadToolRegistry([scalar], Options.Create(new McpReadOptions())));
    }

    [Fact]
    public void ToolLimitUsesTheActualCatalogIncludingApplicationContributors()
    {
        var builtInCount = new McpReadToolRegistry([], Options.Create(new McpReadOptions())).Tools.Count();
        var contributor = new DelegateContributor(builder =>
        {
            for (var index = 0; index < 260; index++)
            {
                builder.Add<BudgetRequest, BudgetResult, BudgetHandler>(
                    $"custom_read_{index}", "Read an application value.", [NgbSystemPermissions.AuditView]);
            }
        });
        var limit = builtInCount + 260;
        var registry = new McpReadToolRegistry([contributor], Options.Create(new McpReadOptions { MaxTools = limit }));
        Assert.Equal(limit, registry.Tools.Count());

        var error = Assert.Throws<InvalidOperationException>(() =>
            new McpReadToolRegistry([contributor], Options.Create(new McpReadOptions { MaxTools = limit - 1 })));
        Assert.Contains(nameof(McpReadOptions.MaxTools), error.Message);
        Assert.Contains((limit - 1).ToString(), error.Message);

        Assert.Throws<InvalidOperationException>(() =>
            new McpReadToolRegistry([], Options.Create(new McpReadOptions { MaxTools = builtInCount - 1 })));
    }

    public sealed class ScalarHandler : IMcpReadToolHandler<string, BudgetResult>
    {
        public Task<BudgetResult> ExecuteAsync(string request, CancellationToken ct)
            => throw new InvalidOperationException("Invalid schemas must fail before execution.");
    }

    [Fact]
    public async Task CatalogIsImmutableAndRejectsUnknownPermissionKeys()
    {
        var contributor = new DelegateContributor(builder => builder.Add<BudgetRequest, BudgetResult, BudgetHandler>(
            "custom_unknown", "Read a custom operation.", [new NgbPermissionKey("catalog", "absent.type", "view")]));
        var registry = new McpReadToolRegistry([contributor], Options.Create(new McpReadOptions()));
        using var permissions = new PermissionDefinitionRegistry([new PlatformPermissionDefinitionSource()]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ValidatePermissionsAsync(permissions, default));
        Assert.Throws<InvalidOperationException>(() => contributor.Contribute(registry));
    }

    [Theory]
    [InlineData("{\"length\":100000}", "result_too_large", 1)]
    [InlineData("{\"length\":-1}", "invalid_input", 0)]
    [InlineData("{\"length\":10,\"extra\":1}", "invalid_input", 0)]
    [InlineData("{\"length\":null}", "invalid_input", 0)]
    public async Task InputAndOutputBudgetsBoundCustomTools(string input, string code, int executions)
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();
        using var document = JsonDocument.Parse(input);
        var error = await Assert.ThrowsAsync<McpReadException>(() => dispatcher.ExecuteAsync("custom_budget", document.RootElement, default));
        Assert.Equal(code, error.Code);
        Assert.Equal(executions, scope.ServiceProvider.GetRequiredService<ExecutionCounter>().Count);
    }

    [Fact]
    public async Task OversizedInputAndUnknownToolsNeverInvokeHandlers()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();
        var input = JsonSerializer.SerializeToElement(new { ignored = new string('x', 70_000) });
        var oversized = await Assert.ThrowsAsync<McpReadException>(() => dispatcher.ExecuteAsync("custom_budget", input, default));
        Assert.Equal("invalid_input", oversized.Code);
        var unknown = await Assert.ThrowsAsync<McpReadException>(() => dispatcher.ExecuteAsync("custom_absent", JsonSerializer.SerializeToElement(new { }), default));
        Assert.Equal("unsupported_tool", unknown.Code);
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<ExecutionCounter>().Count);
    }

    [Fact]
    public async Task SmallCustomResultKeepsItsSchemaAndCancellationPropagates()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();
        var result = await dispatcher.ExecuteAsync("custom_budget", JsonSerializer.SerializeToElement(new { length = 5 }), default);
        Assert.Equal("xxxxx", result.GetProperty("data").GetProperty("text").GetString());
        Assert.Equal(1, result.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(DateTimeKind.Utc, result.GetProperty("generatedAtUtc").GetDateTime().Kind);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.ExecuteAsync("custom_budget",
            JsonSerializer.SerializeToElement(new { length = 5 }), cancelled.Token));
    }

    private static ServiceCollection Services()
    {
        var services = McpTestServices.Create();
        services.Configure<McpReadOptions>(options => options.MaxResponseBytes = 4096);
        services.AddSingleton<IMcpReadToolContributor>(Contributor());
        services.AddScoped<BudgetHandler>();
        services.AddScoped<ExecutionCounter>();
        return services;
    }

    private static IMcpReadToolContributor Contributor()
        => new DelegateContributor(builder => builder.Add<BudgetRequest, BudgetResult, BudgetHandler>(
            "custom_budget", "Read a bounded value.", [NgbSystemPermissions.AuditView]));

    public sealed record BudgetRequest([property: Range(0, 100_000)] int Length);
    public sealed record BudgetResult(string Text);
    public sealed class ExecutionCounter
    {
        public int Count { get; set; }
    }

    public sealed class BudgetHandler(ExecutionCounter counter) : IMcpReadToolHandler<BudgetRequest, BudgetResult>
    {
        public Task<BudgetResult> ExecuteAsync(BudgetRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            counter.Count++;
            return Task.FromResult(new BudgetResult(new string('x', request.Length)));
        }
    }

    public sealed class UntypedHandler : IMcpReadToolHandler<object, BudgetResult>
    {
        public Task<BudgetResult> ExecuteAsync(object request, CancellationToken ct)
            => throw new InvalidOperationException("Untyped registration must fail before invocation.");
    }

    private sealed class DelegateContributor(Action<IMcpReadToolRegistryBuilder> register) : IMcpReadToolContributor
    {
        public void Contribute(IMcpReadToolRegistryBuilder builder) => register(builder);
    }
}
