using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            var knownTypes = new HashSet<(string Kind, string TypeCode)>();

            return new Session(client, tools
                .Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true
                    && tool.ProtocolTool.Meta?[NgbMcpToolMetadata.CompatibilityOnly]?.GetValue<bool>() != true)
                .Select(tool => (AIFunction)new ReadTool(tool, knownTypes))
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

    private sealed class ReadTool(McpClientTool tool, HashSet<(string Kind, string TypeCode)> knownTypes) : AIFunction
    {
        public override string Name => tool.Name;
        public override string Description => tool.Description;
        public override JsonElement JsonSchema => tool.JsonSchema;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            var requested = new Dictionary<string, object?>(arguments, StringComparer.Ordinal);
            var kind = StringArgument(requested, "kind");
            var typeCode = StringArgument(requested, "typeCode");

            if (tool.Name == McpToolNames.ListRecords
                && kind is not null
                && typeCode is not null
                && !knownTypes.Contains((kind, typeCode)))
            {
                return new AiAssistantToolResult(new
                {
                    error = "metadata_required",
                    message = "Read this kind and type's metadata before listing records. Check its declared filters; "
                        + "if they cannot express the requested exhaustive query, do not scan records. "
                        + "For business measures, discover existing reports with ngb_list_reports. Missing record metadata does not establish that reports are unavailable."
                }, [], IsError: true);
            }

            if (tool.Name == McpToolNames.ListRecords
                && !string.IsNullOrWhiteSpace(StringArgument(requested, "cursor"))
                && string.IsNullOrWhiteSpace(StringArgument(requested, "search"))
                && !HasFilters(requested))
            {
                return new AiAssistantToolResult(new
                {
                    error = "filter_required",
                    message = "Unfiltered record scans are not available in Assistant. Use declared filters or text search before continuing a record list. For an exact count, request includeTotal on the first page; for business aggregates, use a report."
                }, [], IsError: true);
            }

            if (tool.Name is McpToolNames.ListRecords
                or McpToolNames.ListNotes
                or McpToolNames.ListAttachments
                or McpToolNames.GetAuditHistory)
            {
                requested["limit"] = PageSize(requested.GetValueOrDefault("limit"));
            }
            else if (tool.Name == McpToolNames.ExecuteReport
                && requested.TryGetValue("request", out var reportRequest)
                && JsonSerializer.SerializeToNode(reportRequest, Json) is JsonObject report)
            {
                report["limit"] = JsonSerializer.SerializeToNode(PageSize(report["limit"], 200), Json);
                requested["request"] = report;
            }

            var result = await tool.CallAsync(requested, cancellationToken: cancellationToken);
            var text = result.Content.OfType<TextContentBlock>().Select(с => с.Text).ToArray();
            
            if (result.IsError == true)
                return new AiAssistantToolResult(new { error = true, content = text }, [], IsError: true);

            if (result.StructuredContent is not { } content)
                return new AiAssistantToolResult(text, []);

            var response = content.Deserialize<McpReadResponse<JsonElement>>(Json);
            var sources = response?.Sources.Select(source => new AiAssistantSource(
                source.Kind.ToCode(),
                source.TypeCode,
                source.Id,
                source.Title)).ToArray() ?? [];

            if (tool.Name == McpToolNames.GetRecord)
            {
                sources = sources.SelectMany(source => new[]
                {
                    source,
                    source with { Title = "Audit history: " + source.Title[..Math.Min(source.Title.Length, 241)], View = "audit" }
                }).ToArray();
            }

            // The envelope is transport metadata. Pass business data once; sources are handled
            // separately by the runner and are never inferred from arbitrary payload fields.
            var data = response?.Data ?? content;
            var projected = McpAssistantProjection.Create(tool.Name, data, requested);
            if (tool.Name == McpToolNames.GetMetadata && kind is not null && typeCode is not null)
                knownTypes.Add((kind, typeCode));

            return new AiAssistantToolResult(projected, sources);
        }

        private static string? StringArgument(IDictionary<string, object?> arguments, string name)
        {
            arguments.TryGetValue(name, out var argument);
            var value = JsonSerializer.SerializeToElement(argument, Json);
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static bool HasFilters(IDictionary<string, object?> arguments)
        {
            arguments.TryGetValue("filters", out var argument);
            var filters = JsonSerializer.SerializeToElement(argument, Json);

            return filters.ValueKind == JsonValueKind.Object && filters.EnumerateObject().Any(property =>
                property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()));
        }

        private static object PageSize(object? requested, int maximum = 100)
        {
            if (requested is null)
                return 10;

            var value = JsonSerializer.SerializeToElement(requested, Json);

            // Preserve invalid inputs for the MCP validator. Valid pages retain their
            // cursor/completeness metadata; a smaller page never becomes a complete list.
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var limit) && limit > 0 && limit <= maximum
                ? Math.Min(limit, 20)
                : value;
        }
    }
}
