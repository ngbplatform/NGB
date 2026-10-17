using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NGB.Mcp;
using NGB.Mcp.AspNetCore;
using NGB.Mcp.Tools;

namespace NGB.AiAssistant.AspNetCore;

internal sealed class McpToolSessionFactory(
    IHttpContextAccessor context,
    IHttpClientFactory clients,
    IServiceProvider services)
    : IAiAssistantToolSessionFactory
{
    internal const string ClientName = "NGB.AiAssistant.Mcp";

    private static readonly string ClientVersion = typeof(McpToolSessionFactory)
        .Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion.Split('+')[0];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public async Task<IAiAssistantToolSession> OpenAsync(CancellationToken ct)
    {
        var authorization = context.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
            throw new AiAssistantException("authentication_required", "Please sign in again.");

        var http = clients.CreateClient(ClientName);
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = services.GetNgbMcpEndpoint(),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { [HeaderNames.Authorization] = authorization }
        }, http, ownsHttpClient: true);

        McpClient? client = null;
        try
        {
            client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "NGB AI Assistant", Version = ClientVersion }
            }, cancellationToken: ct);

            var tools = await client.ListToolsAsync(cancellationToken: ct);

            return new Session(client, tools
                .Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true
                    && tool.ProtocolTool.Meta?[NgbMcpToolMetadata.CompatibilityOnly]?.GetValue<bool>() != true)
                .Select(tool => (AIFunction)new ReadTool(tool))
                .ToArray());
        }
        catch
        {
            if (client is not null)
                await client.DisposeAsync();
            else
                await transport.DisposeAsync();

            throw;
        }
    }

    private sealed class Session(McpClient client, IReadOnlyList<AIFunction> tools) : IAiAssistantToolSession
    {
        public IReadOnlyList<AIFunction> Tools { get; } = tools;

        public ValueTask DisposeAsync() => client.DisposeAsync();
    }

    private sealed class ReadTool(McpClientTool tool) : AIFunction
    {
        public override string Name => tool.Name;
        public override string Description => tool.Description;
        public override JsonElement JsonSchema => tool.JsonSchema;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            var result = await tool.CallAsync(arguments, cancellationToken: cancellationToken);
            var text = result.Content.OfType<TextContentBlock>().Select(с => с.Text).ToArray();
            
            if (result.IsError == true)
                return new AiAssistantToolResult(new { error = true, content = text }, []);

            if (result.StructuredContent is not { } content)
                return new AiAssistantToolResult(text, []);

            var response = content.Deserialize<McpReadResponse<JsonElement>>(Json);
            var sources = response?.Sources.Select(source => new AiAssistantSource(
                source.Kind.ToCode(),
                source.TypeCode,
                source.Id,
                source.Title)).ToArray() ?? [];

            return new AiAssistantToolResult(content, sources);
        }
    }
}
