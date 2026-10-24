using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using NGB.Core.Security;
using NGB.Mcp.AspNetCore;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpProtocolHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtocolErrorsRemainSanitizedWithOrWithoutATrace(bool traced)
    {
        var services = McpTestServices.Create();
        services.AddNgbMcpContributor<Contributor>();
        services.AddScoped<FaultHandler>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = new RequestContext<CallToolRequestParams>(Mock.Of<McpServer>(),
            new JsonRpcRequest { Id = new RequestId(1), Method = "tools/call" },
            new CallToolRequestParams
            {
                Name = "custom_fault",
                Arguments = new Dictionary<string, JsonElement> { ["mode"] = JsonSerializer.SerializeToElement("json") }
            }) { Services = scope.ServiceProvider };
        var original = Activity.Current;
        try
        {
            Activity.Current = null;
            using var activity = traced ? new Activity("mcp-error-test").Start() : null;
            var result = await McpProtocolHandlers.CallAsync(context, default);
            Assert.True(result.IsError);
            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            using var error = JsonDocument.Parse(text);
            Assert.Equal("internal_error", error.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("secret-database", text);
            Assert.InRange(text.Length, 1, 600);
        }
        finally
        {
            Activity.Current = original;
        }
    }

    public sealed record FaultRequest(string Mode);
    public sealed record FaultResult;

    public sealed class FaultHandler : IMcpReadToolHandler<FaultRequest, FaultResult>
    {
        public Task<FaultResult> ExecuteAsync(FaultRequest request, CancellationToken ct)
            => throw new JsonException("secret-database");
    }

    public sealed class Contributor : IMcpReadToolContributor
    {
        public void Contribute(IMcpReadToolRegistryBuilder builder)
            => builder.Add<FaultRequest, FaultResult, FaultHandler>("custom_fault", "Read an error fixture.", [NgbSystemPermissions.AuditView]);
    }
}
