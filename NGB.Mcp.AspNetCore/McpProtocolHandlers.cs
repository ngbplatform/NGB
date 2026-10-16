using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NGB.Tools.Exceptions;

namespace NGB.Mcp.AspNetCore;

internal static class McpProtocolHandlers
{
    private static readonly Meter Meter = new("NGB.Mcp");
    private static readonly Counter<long> Calls = Meter.CreateCounter<long>("ngb.mcp.calls");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("ngb.mcp.duration", "s");

    internal static async ValueTask<ListToolsResult> ListAsync(
        RequestContext<ListToolsRequestParams> context,
        CancellationToken ct)
    {
        var descriptions = await context.Services!.GetRequiredService<McpReadDispatcher>()
            .ListAsync(ct);

        return new ListToolsResult
        {
            Tools = descriptions.Select(tool => new Tool
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = tool.InputSchema,
                    OutputSchema = tool.OutputSchema,
                    Annotations = new ToolAnnotations
                    {
                        ReadOnlyHint = true,
                        DestructiveHint = false,
                        IdempotentHint = true,
                        OpenWorldHint = false
                    }
                })
                .ToArray()
        };
    }

    internal static async ValueTask<CallToolResult> CallAsync(
        RequestContext<CallToolRequestParams> context,
        CancellationToken ct)
    {
        var services = context.Services!;
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";

        try
        {
            var input = JsonSerializer.SerializeToElement(context.Params.Arguments
                ?? new Dictionary<string, JsonElement>());
            var data = await services.GetRequiredService<McpReadDispatcher>().ExecuteAsync(context.Params.Name, input, ct);
            var result = new CallToolResult
            {
                StructuredContent = data,
                Content = [new TextContentBlock { Text = data.GetRawText() }]
            };

            if (JsonSerializer.SerializeToUtf8Bytes(result).Length > services.GetRequiredService<IOptions<McpReadOptions>>().Value.MaxResponseBytes)
                throw new McpReadException("result_too_large", "Narrow the query or request a smaller page.");

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        catch (Exception exception)
        {
            outcome = "error";
            if (exception is McpReadException { Code: "unsupported_tool" })
                throw new McpProtocolException("The requested read tool is unavailable.", McpErrorCode.InvalidParams);

            var error = exception switch
            {
                McpReadException { Code.Length: <= 64, Message.Length: <= 512 } known => new McpReadError(known.Code, known.Message),
                NgbForbiddenException or NgbNotFoundException => new McpReadError("unavailable", "The requested resource is unavailable."),
                NgbArgumentInvalidException { ParamName: "cursor" } => new McpReadError("invalid_cursor", "The continuation cursor is invalid for this query."),
                NgbValidationException or ArgumentException => new McpReadError("invalid_input", "The request does not match the supported query contract."),
                _ => new McpReadError("internal_error", "The read operation could not be completed.")
            };

            if (error.Code == "internal_error")
            {
                services
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("NGB.Mcp")
                    .LogError("MCP read failed with {ExceptionType}; trace {TraceId}.", exception.GetType().Name, Activity.Current?.TraceId);
            }
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(error, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                }]
            };
        }
        finally
        {
            var tags = new TagList
            {
                { "outcome", outcome }
            };
            Calls.Add(1, tags);
            Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        }
    }
}
