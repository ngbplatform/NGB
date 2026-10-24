using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Mcp;
using NGB.Mcp.AspNetCore;
using NGB.Runtime.Security;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed partial class AssistantMcpTransportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionRequiresRequestAuthenticationBeforeCreatingATransport(bool hasContext)
    {
        var clients = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var accessor = new HttpContextAccessor { HttpContext = hasContext ? new DefaultHttpContext() : null };
        using var services = new ServiceCollection().BuildServiceProvider();
        var factory = new McpToolSessionFactory(accessor, clients.Object, services, Options.Create(new AiAssistantHttpOptions()));

        var error = await Assert.ThrowsAsync<AiAssistantException>(() => factory.OpenAsync(default));

        Assert.Equal("authentication_required", error.Code);
        clients.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SessionIncludesOnlyReadOnlyNonCompatibilityTools()
    {
        await using var fixture = await TransportFixture.CreateAsync();
        await using var session = await fixture.Factory.OpenAsync(default);

        Assert.DoesNotContain(session.Tools, tool => tool.Name is "write" or "unannotated" or "compatibility");
        Assert.Contains(session.Tools, tool => tool.Name == "custom_read");
        Assert.All(fixture.Handler.Authorizations, value => Assert.Equal("Bearer request-token", value));
        Assert.Null(await ((IAiAssistantSourceResolver)session).ResolveAsync(new("document", "sample.record", Guid.NewGuid(), "Record"), default));
    }

    [Theory]
    [InlineData("initialize")]
    [InlineData("tools/list")]
    public async Task FailedSessionInitializationDisposesTheTransport(string failure)
    {
        await using var fixture = await TransportFixture.CreateAsync();
        fixture.Handler.FailingMethod = failure;

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Factory.OpenAsync(default));

        Assert.True(fixture.Handler.Disposed);
    }

    [Fact]
    public async Task ClosingASuccessfulSessionDisposesItsHttpTransport()
    {
        await using var fixture = await TransportFixture.CreateAsync();
        var session = await fixture.Factory.OpenAsync(default);

        await session.DisposeAsync();

        Assert.True(fixture.Handler.Disposed);
    }

    [Theory]
    [InlineData("plain failure", "read_failed")]
    [InlineData("null", "read_failed")]
    [InlineData("{\"code\":\"invalid_input\",\"message\":\"Invalid limit\"}", "invalid_input")]
    public async Task ToolFailuresPreserveRecoveryGuidanceWithoutSupplyingEvidence(string error, string expected)
    {
        await using var fixture = await TransportFixture.CreateAsync();
        fixture.Handler.ToolResult = new { isError = true, content = new[] { new { type = "text", text = error } } };
        await using var session = await fixture.Factory.OpenAsync(default);

        var result = Assert.IsType<AiAssistantToolResult>(await session.Tools.Single(tool => tool.Name == "custom_read").InvokeAsync(new()));

        Assert.True(result.IsError);
        Assert.Empty(result.Sources);
        var data = JsonSerializer.SerializeToElement(result.Data);
        Assert.Equal(expected, data.GetProperty("error").GetString());
        Assert.Equal(expected == "invalid_input", data.GetProperty("inputSchema").ValueKind == JsonValueKind.Object);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnstructuredToolContentIsPreservedWithoutInventingSources(bool explicitNull)
    {
        await using var fixture = await TransportFixture.CreateAsync();
        var response = new Dictionary<string, object?>
        {
            ["content"] = new[] { new { type = "text", text = "Authorized result" } }
        };
        if (explicitNull)
            response["structuredContent"] = null;
        fixture.Handler.ToolResult = response;
        await using var session = await fixture.Factory.OpenAsync(default);

        var result = Assert.IsType<AiAssistantToolResult>(await session.Tools.Single(tool => tool.Name == "custom_read").InvokeAsync(new()));

        Assert.False(result.IsError);
        Assert.Equal(new[] { "Authorized result" }, Assert.IsType<string[]>(result.Data));
        Assert.Empty(result.Sources);
    }

    [Theory]
    [InlineData("null", 10L)]
    [InlineData("5", 5L)]
    [InlineData("100", 20L)]
    [InlineData("0", 0L)]
    [InlineData("101", 101L)]
    [InlineData("2147483648", 2147483648L)]
    [InlineData("\"bad\"", null)]
    public async Task PageBoundsPreserveInvalidValuesForTheMcpValidator(string limit, long? expected)
    {
        await using var fixture = await TransportFixture.CreateAsync();
        await using var session = await fixture.Factory.OpenAsync(default);
        var arguments = new AIFunctionArguments { ["limit"] = JsonSerializer.Deserialize<object>(limit) };

        await session.Tools.Single(tool => tool.Name == McpToolNames.ListNotes).InvokeAsync(arguments);

        var sent = Assert.Single(fixture.Handler.Calls).GetProperty("arguments").GetProperty("limit");
        if (expected is not null)
            Assert.Equal(expected, sent.GetInt64());
        else
            Assert.Equal("bad", sent.GetString());
    }

    [Theory]
    [InlineData("\"other\"")]
    [InlineData("42")]
    [InlineData("null")]
    public async Task InvalidReportViewsAreRejectedBeforeCallingMcp(string view)
    {
        await using var fixture = await TransportFixture.CreateAsync();
        await using var session = await fixture.Factory.OpenAsync(default);
        var arguments = new AIFunctionArguments
        {
            ["reportCode"] = "sample.report",
            ["request"] = new { },
            ["resultView"] = JsonSerializer.Deserialize<object>(view)
        };

        var result = Assert.IsType<AiAssistantToolResult>(await session.Tools.Single(tool => tool.Name == McpToolNames.ExecuteReport).InvokeAsync(arguments));

        Assert.True(result.IsError);
        Assert.Contains("invalid_input", JsonSerializer.Serialize(result.Data));
        Assert.Empty(fixture.Handler.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"request\":null}")]
    [InlineData("{\"request\":{\"limit\":1}}")]
    [InlineData("{\"request\":{\"limit\":\"bad\"}}")]
    [InlineData("{\"request\":{\"limit\":2147483648}}")]
    [InlineData("{\"request\":{\"cursor\":\"unrecognized\"}}")]
    public async Task UnsupportedPageReductionLeavesTheOriginalArgumentsUnchanged(string json)
    {
        await using var fixture = await TransportFixture.CreateAsync();
        await using var session = await fixture.Factory.OpenAsync(default);
        var arguments = new AIFunctionArguments(JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!);
        var tool = Assert.IsAssignableFrom<IAiAssistantPagedTool>(session.Tools.Single(tool => tool.Name == McpToolNames.ExecuteReport));

        Assert.False(tool.TryReducePage(arguments, out var reduced));
        Assert.Equal(JsonSerializer.Serialize(arguments), JsonSerializer.Serialize(reduced));
        Assert.False(((IAiAssistantPagedTool)session.Tools.Single(tool => tool.Name == "custom_read")).TryReducePage(arguments, out _));
        Assert.Empty(fixture.Handler.Calls);
    }

    private sealed class TransportFixture(WebApplication app, ProtocolHandler handler, McpToolSessionFactory factory) : IAsyncDisposable
    {
        internal ProtocolHandler Handler => handler;
        internal McpToolSessionFactory Factory => factory;

        internal static Task<TransportFixture> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Host.UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureManagement:Mcp"] = "true",
                ["Mcp:Resource"] = "https://application.example/mcp"
            });
            builder.Services.AddScoped(_ => new Mock<INgbAccessChecker>(MockBehavior.Strict).Object);
            McpTestServices.AddReads(builder.Services);
            builder.Services.AddNgbMcp(builder.Configuration);
            var app = builder.Build();
            app.MapNgbMcp();
            var handler = new ProtocolHandler();
            var clients = new Mock<IHttpClientFactory>(MockBehavior.Strict);
            clients.Setup(value => value.CreateClient(McpToolSessionFactory.ClientName)).Returns(() => new HttpClient(handler));
            var context = new DefaultHttpContext();
            context.Request.Headers.Authorization = "Bearer request-token";
            var factory = new McpToolSessionFactory(new HttpContextAccessor { HttpContext = context }, clients.Object,
                app.Services, Options.Create(new AiAssistantHttpOptions()));

            return Task.FromResult(new TransportFixture(app, handler, factory));
        }

        public async ValueTask DisposeAsync()
        {
            handler.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class ProtocolHandler : HttpMessageHandler
    {
        internal string? FailingMethod { get; set; }
        internal bool Disposed { get; private set; }
        internal List<string?> Authorizations { get; } = [];
        internal List<JsonElement> Calls { get; } = [];
        internal object ToolResult { get; set; } = new
        {
            content = Array.Empty<object>(),
            structuredContent = new { schemaVersion = 1, data = new { value = "Result" }, sources = Array.Empty<object>() }
        };
        internal List<object> Tools { get; } =
        [
            Tool("custom_read"),
            Tool(McpToolNames.ListNotes),
            Tool(McpToolNames.ExecuteReport),
            Tool(McpToolNames.ListRecords),
            Tool(McpToolNames.GetMetadata),
            Tool("write", false),
            Tool("unannotated", null),
            Tool("compatibility", true, true)
        ];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.Authorization?.ToString());
            if (request.Method != HttpMethod.Post)
                return new(HttpStatusCode.MethodNotAllowed);

            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var method = body.GetProperty("method").GetString();
            if (method == FailingMethod)
                return new(HttpStatusCode.InternalServerError);
            if (!body.TryGetProperty("id", out var id))
                return new(HttpStatusCode.Accepted);

            if (method == "server/discover")
            {
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        jsonrpc = "2.0",
                        id,
                        error = new { code = -32601, message = "Method not found" }
                    })
                };
            }

            object result;
            if (method == "initialize")
            {
                result = new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { tools = new { } },
                    serverInfo = new { name = "fixture", version = "1" }
                };
            }
            else if (method == "tools/list")
            {
                result = new { tools = Tools };
            }
            else
            {
                Assert.Equal("tools/call", method);
                Calls.Add(body.GetProperty("params").Clone());
                result = ToolResult;
            }

            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { jsonrpc = "2.0", id, result }) };
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        internal static object Tool(string name, bool? readOnly = true, bool? compatibility = null)
            => new
            {
                name,
                description = "Read fixture data.",
                inputSchema = new { type = "object", properties = new { } },
                annotations = readOnly is null ? null : new { readOnlyHint = readOnly },
                _meta = compatibility is null ? null : new Dictionary<string, object?> { [NgbMcpToolMetadata.CompatibilityOnly] = compatibility }
            };
    }
}
