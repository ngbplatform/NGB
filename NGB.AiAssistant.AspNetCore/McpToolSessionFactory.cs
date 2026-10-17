using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NGB.Mcp.AspNetCore;

namespace NGB.AiAssistant.AspNetCore;

internal sealed class McpToolSessionFactory(
    IHttpContextAccessor context,
    IHttpClientFactory clients,
    IServiceProvider services)
    : IAiAssistantToolSessionFactory
{
    internal const string ClientName = "NGB.AiAssistant.Mcp";

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
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = authorization }
        }, http, ownsHttpClient: true);

        McpClient? client = null;
        try
        {
            client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "NGB AI Assistant", Version = "3.3.0" }
            }, cancellationToken: ct);

            var tools = await client.ListToolsAsync(cancellationToken: ct);

            return new Session(client, tools
                .Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true)
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
            if (result.IsError == true)
                return new { error = true, content = result.Content.OfType<TextContentBlock>().Select(content => content.Text).ToArray() };

            var content = result.StructuredContent
                ?? JsonSerializer.SerializeToElement(result.Content.OfType<TextContentBlock>()
                    .Select(content => content.Text)
                    .ToArray());
            
            if (tool.Name == "ngb_execute_report")
            {
                var request = JsonSerializer.SerializeToElement(arguments);
                if (request.TryGetProperty("reportCode", out var code) && code.ValueKind == JsonValueKind.String)
                {
                    // Execution results contain a sheet, but no report identifier. Add the source only after a successful authorized read.
                    return new
                    {
                        data = content,
                        source = new AiAssistantSource("report", code.GetString()!, null, code.GetString()!)
                    };
                }
            }

            return content;
        }
    }
}
