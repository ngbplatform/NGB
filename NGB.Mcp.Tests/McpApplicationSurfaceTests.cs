using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Application.Abstractions.Features;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpApplicationSurfaceTests
{
    [Fact]
    public void PlatformDocumentationIsNotEmbeddedInMcp()
    {
        var resources = typeof(McpReadDispatcher).Assembly.GetManifestResourceNames();
        Assert.DoesNotContain(resources, name => name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("NGB.Mcp.Knowledge.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentationToolsAreUnavailableForUsersAndAdministrators(bool administrator)
    {
        var services = McpTestServices.Create(administrator);
        var features = new Mock<INgbFeatureService>();
        features.Setup(value => value.IsEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        services.AddSingleton(features.Object);
        services.Configure<McpReadOptions>(options => options.MaxTools = 18);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();
        var tools = await dispatcher.ListAsync(default);
        var capabilities = await dispatcher.ExecuteAsync("ngb_get_capabilities", JsonSerializer.SerializeToElement(new { }), default);
        var advertisedTools = capabilities.GetProperty("data").GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetString()).ToArray();

        foreach (var name in new[] { "ngb_search_platform_knowledge", "ngb_get_platform_knowledge" })
        {
            Assert.DoesNotContain(tools, tool => tool.Name == name);
            Assert.DoesNotContain(name, advertisedTools);
            var error = await Assert.ThrowsAsync<McpReadException>(() => dispatcher.ExecuteAsync(name,
                JsonSerializer.SerializeToElement(new { id = "security-and-permissions", query = "permissions" }), default));
            Assert.Equal("unsupported_tool", error.Code);
        }
    }
}
