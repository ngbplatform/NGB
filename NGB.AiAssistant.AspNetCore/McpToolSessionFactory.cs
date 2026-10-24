using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
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
    IServiceProvider services,
    IOptions<AiAssistantHttpOptions> options)
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

            return new Session(client, transport, tools
                .Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true
                    && tool.ProtocolTool.Meta?[NgbMcpToolMetadata.CompatibilityOnly]?.GetValue<bool>() != true)
                .Select(tool => (AIFunction)new ReadTool(tool, knownTypes, options.Value.ReportPageSize))
                .ToArray());
        }
        catch
        {
            try
            {
                if (client is not null)
                    await client.DisposeAsync();
            }
            finally
            {
                await transport.DisposeAsync();
            }

            throw;
        }
    }

    private sealed class Session(McpClient client, HttpClientTransport transport, IReadOnlyList<AIFunction> tools)
        : IAiAssistantToolSession, IAiAssistantSourceResolver
    {
        public IReadOnlyList<AIFunction> Tools { get; } = tools;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await client.DisposeAsync();
            }
            finally
            {
                await transport.DisposeAsync();
            }
        }

        public async Task<AiAssistantSource?> ResolveAsync(AiAssistantSource candidate, CancellationToken ct)
        {
            var name = candidate.Kind == "report" ? McpToolNames.GetReportDefinition : McpToolNames.GetRecord;
            var tool = Tools.FirstOrDefault(item => item.Name == name);

            if (tool is null)
                return null;

            var arguments = candidate.Kind == "report"
                ? new AIFunctionArguments { ["reportCode"] = candidate.TypeCode }
                : new AIFunctionArguments { ["kind"] = candidate.Kind, ["typeCode"] = candidate.TypeCode, ["id"] = candidate.Id };

            var result = await tool.InvokeAsync(arguments, ct);

            return result is AiAssistantToolResult { IsError: false } read
                ? read.Sources.FirstOrDefault(source => source.Reference == candidate.Reference)
                : null;
        }
    }

    private sealed class ReadTool(
        McpClientTool tool,
        HashSet<(string Kind, string TypeCode)> knownTypes,
        int reportPageSize)
        : AIFunction, IAiAssistantPagedTool
    {
        private readonly AssistantReportContinuations _continuations = new();
        private readonly HashSet<(string? Kind, string? TypeCode, string Cursor)> _unfilteredCursors = new();
        private readonly JsonElement _schema = CreateSchema(tool);

        public override string Name => tool.Name;
        public override string Description => tool.Description;
        public override JsonElement JsonSchema => _schema;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            var requested = new Dictionary<string, object?>(arguments, StringComparer.Ordinal);

            if (tool.Name == McpToolNames.ExecuteReport
                && requested.ContainsKey("resultView")
                && StringArgument(requested, "resultView") is not ("full" or "totals"))
            {
                return new AiAssistantToolResult(new
                    {
                        error = "invalid_input",
                        message = "resultView must be full or totals. It selects returned evidence, not report calculations."
                    },
                    [],
                    IsError: true);
            }

            if (tool.Name == McpToolNames.ExecuteReport && !_continuations.TryResolve(requested, out requested))
            {
                return new AiAssistantToolResult(new
                    {
                        error = "invalid_cursor",
                        message = "Use the exact nextCursor from this turn with the same reportCode and no changed selection. "
                            + "Pass request.cursor only, plus an optional smaller limit. To change criteria, start without a cursor."
                    },
                    [],
                    IsError: true);
            }

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

            if (tool.Name == McpToolNames.ListRecords && StringArgument(requested, "cursor") is { Length: > 0 } cursor)
            {
                if (_unfilteredCursors.Contains((kind, typeCode, cursor)))
                {
                    return new AiAssistantToolResult(new
                        {
                            error = "unfiltered_scan",
                            message = "This unfiltered scan cannot continue. The first page does not establish the requested selection. "
                                + "Do not work around the limit by searching generic labels or inventing filters. "
                                + "Explain which requested result remains unavailable; do not substitute matching sample rows."
                        }, 
                        [],
                        IsError: true)
                    {
                        ErrorCode = "unfiltered_scan"
                    };
                }

                if (string.IsNullOrWhiteSpace(StringArgument(requested, "search")) && !HasFilters(requested))
                {
                    return new AiAssistantToolResult(new
                        {
                            error = "filter_required",
                            message = "Preserve the preceding page's search and declared filters when continuing a filtered list. "
                                + "Do not invent a different selection or continue an unfiltered scan."
                        },
                        [],
                        IsError: true);
                }
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
                // Preserve an explicitly requested page. The MCP service validates its
                // bound; the runner reduces it only if the actual result is too large.
                report["limit"] ??= DefaultReportPageSize(requested);
                requested["request"] = report;
            }

            // resultView belongs only to the assistant's evidence projection. The
            // authorized report service receives its existing request unchanged.
            var applicationArguments = new Dictionary<string, object?>(requested, StringComparer.Ordinal);
            if (tool.Name == McpToolNames.ExecuteReport)
                applicationArguments.Remove("resultView");

            var result = await tool.CallAsync(applicationArguments, cancellationToken: cancellationToken);
            var text = result.Content.OfType<TextContentBlock>().Select(с => с.Text).ToArray();
            
            if (result.IsError == true)
            {
                var error = text
                    .Select(ReadError)
                    .FirstOrDefault(value => value is not null);

                var invalidInput = error?.Code == "invalid_input";

                return new AiAssistantToolResult(new
                    {
                        error = error?.Code ?? "read_failed",
                        message = error?.Message,
                        content = error is null ? text : null,
                        inputSchema = invalidInput ? tool.JsonSchema : (JsonElement?)null,
                        recovery = invalidInput
                            ? "The submitted arguments are invalid, not the capability. Correct the indicated path using the input schema "
                                + "and definition, preserving the requested scope. Do not repeat the same arguments or report the operation as unsupported."
                            : "This read failed and supplies no business evidence. Do not retry it with different paging arguments "
                                + "or restart the same selection. Retain other successful evidence; a failed page leaves the population incomplete."
                    },
                    [],
                    IsError: true)
                {
                    ErrorCode = error?.Code
                };
            }

            if (result.StructuredContent is not { } content)
                return new AiAssistantToolResult(text, []);

            var response = content.Deserialize<McpReadResponse<JsonElement>>(Json)!;
            var sources = response.Sources.Select(source => new AiAssistantSource(
                source.Kind.ToCode(),
                source.TypeCode,
                source.Id,
                source.Title)).ToArray();

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
            var data = response.Data;
            if (data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return new AiAssistantToolResult(new
                    {
                        error = "empty_result",
                        message = "The operation returned no result. This does not establish an empty list, zero or absence. "
                            + "Use another available read if it can answer the question."
                    },
                    [],
                    IsError: true);
            }

            var projected = McpAssistantProjection.Create(tool.Name, data, requested);

            if (tool.Name == McpToolNames.ListRecords)
            {
                var records = data.Deserialize<RecordsResult>(Json)!;
                var nextCursor = records.Catalogs?.NextCursor
                    ?? records.Documents?.NextCursor
                    ?? records.GeneralJournalEntries?.NextCursor;

                if (!string.IsNullOrWhiteSpace(nextCursor))
                {
                    var selection = (kind, typeCode, nextCursor);
                    if (string.IsNullOrWhiteSpace(StringArgument(requested, "search")) && !HasFilters(requested))
                        _unfilteredCursors.Add(selection);
                    else
                        _unfilteredCursors.Remove(selection);
                }
            }

            if (tool.Name == McpToolNames.ExecuteReport)
                projected = _continuations.Publish(projected, requested);

            if (tool.Name == McpToolNames.GetMetadata && kind is not null && typeCode is not null)
                knownTypes.Add((kind, typeCode));

            return new AiAssistantToolResult(projected, sources);
        }

        public bool TryReducePage(AIFunctionArguments arguments, out AIFunctionArguments reduced)
        {
            reduced = new AIFunctionArguments(arguments);
            var requested = new Dictionary<string, object?>(arguments, StringComparer.Ordinal);

            if (tool.Name == McpToolNames.ExecuteReport
                && _continuations.TryResolve(requested, out requested)
                && requested.TryGetValue("request", out var resolved)
                && JsonSerializer.SerializeToNode(resolved, Json) is JsonObject selection
                && arguments.TryGetValue("request", out var value)
                && JsonSerializer.SerializeToNode(value, Json) is JsonObject report)
            {
                var limit = JsonSerializer.SerializeToElement(
                    selection["limit"] ?? JsonValue.Create(DefaultReportPageSize(requested)), Json);
                if (limit.ValueKind != JsonValueKind.Number || !limit.TryGetInt32(out var size) || size <= 1)
                    return false;

                // Reduce the effective page while retaining the model's continuation handle.
                report["limit"] = Math.Max(1, size / 2);
                reduced["request"] = report;

                return true;
            }

            return false;
        }

        private static JsonElement CreateSchema(McpClientTool tool)
        {
            if (tool.Name != McpToolNames.ExecuteReport)
                return tool.JsonSchema;

            var schema = JsonNode.Parse(tool.JsonSchema.GetRawText())!.AsObject();
            schema["properties"]!["resultView"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("full", "totals"),
                ["description"] = "Use totals when the question needs only existing report total/subtotal rows; "
                    + "detail rows are omitted without calculating new totals. Omit request.limit to use the maximum permitted page size. "
                    + "Follow nextCursor until the requested total is returned. "
                    + "Use full (default) for entries or category breakdowns. Continuations retain this view."
            };

            return JsonSerializer.SerializeToElement(schema, Json);
        }

        private int DefaultReportPageSize(IDictionary<string, object?> arguments)
            => StringArgument(arguments, "resultView") == "totals"
                ? McpReadOptions.MaximumReportPageSize
                : reportPageSize;

        private static McpReadError? ReadError(string value)
        {
            try
            {
                return JsonSerializer.Deserialize<McpReadError>(value, Json);
            }
            catch (JsonException)
            {
                return null;
            }
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
