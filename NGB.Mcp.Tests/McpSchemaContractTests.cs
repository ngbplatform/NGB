using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Application.Abstractions.Features;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpSchemaContractTests
{
    [Fact]
    public async Task ToolNamesAndSchemasMatchTheVersionedPublicContract()
    {
        var services = McpTestServices.Create();
        var features = new Mock<INgbFeatureService>();
        features.Setup(value => value.IsEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        services.AddSingleton(features.Object);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var tools = await scope.ServiceProvider.GetRequiredService<McpReadDispatcher>().ListAsync(default);
        Assert.Equal(18, tools.Count);
        var actual = JsonSerializer.SerializeToNode(tools, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        var update = Environment.GetEnvironmentVariable("NGB_MCP_SCHEMA_OUTPUT");
        if (!string.IsNullOrEmpty(update))
        {
            await File.WriteAllTextAsync(update, actual!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            return;
        }
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "tool-contract-3.3.0.json")));
        Assert.True(JsonNode.DeepEquals(expected, actual), "The public tool contract changed. Review and explicitly regenerate the versioned schema fixture.");
    }
}
