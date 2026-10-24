using System.Text.Json;
using System.Text.Json.Nodes;
using NGB.Mcp;

namespace NGB.AiAssistant.AspNetCore;

/// <summary>Keeps opaque application cursors and their selection together within one authorized tool session.</summary>
internal sealed class AssistantReportContinuations
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _prefix = "page-" + Guid.NewGuid().ToString("N")[..12];
    private readonly Dictionary<string, JsonObject> _requests = new(StringComparer.Ordinal);

    internal bool TryResolve(Dictionary<string, object?> arguments, out Dictionary<string, object?> resolved)
    {
        resolved = arguments;
        var proposed = JsonSerializer.SerializeToNode(arguments, Json)!.AsObject();

        if (proposed["request"] is not JsonObject request
            || request["cursor"] is not JsonValue cursorValue
            || !cursorValue.TryGetValue<string>(out var cursor)
            || string.IsNullOrWhiteSpace(cursor))
        {
            return true;
        }

        if (!_requests.TryGetValue(cursor, out var saved)
            || !JsonNode.DeepEquals(proposed["reportCode"], saved["reportCode"])
            || (proposed["resultView"] is { } view && !JsonNode.DeepEquals(view, saved["resultView"] ?? JsonValue.Create("full"))))
        {
            return false;
        }

        var original = saved["request"]!.AsObject();
        foreach (var property in request)
        {
            if (property.Key is "cursor" or "limit" or "offset" || property.Value is null)
                continue;

            if (!JsonNode.DeepEquals(property.Value, original[property.Key]))
                return false;
        }

        var restored = saved.DeepClone().AsObject();

        if (request["limit"] is { } limit)
        {
            // Keep the page size already proven to fit. A model may repeat its
            // original larger limit after the runner adapted an oversized page.
            var retainedLimit = limit.DeepClone();
            if (limit is JsonValue proposedLimit
                && proposedLimit.TryGetValue<int>(out var size)
                && size is > 0 and <= McpReadOptions.MaximumReportPageSize
                && original["limit"] is JsonValue savedLimit
                && savedLimit.TryGetValue<int>(out var previousSize))
            {
                retainedLimit = JsonValue.Create(Math.Min(size, previousSize));
            }

            restored["request"]!["limit"] = retainedLimit;
        }

        resolved = restored.Deserialize<Dictionary<string, object?>>(Json)!;

        return true;
    }

    internal JsonElement Publish(JsonElement result, Dictionary<string, object?> arguments)
    {
        var projected = JsonSerializer.SerializeToNode(result, Json)!.AsObject();
        if (projected["nextCursor"] is JsonValue value && value.TryGetValue<string>(out var cursor)
            && !string.IsNullOrWhiteSpace(cursor))
        {
            var saved = JsonSerializer.SerializeToNode(arguments, Json)!.AsObject();
            saved["request"]!["cursor"] = cursor;
            var handle = _prefix + "-" + _requests.Count;
            _requests.Add(handle, saved);
            projected["nextCursor"] = handle;
        }

        // The model receives the selection and a short handle, never a second copy of the native cursor.
        if (projected["criteria"]?["request"] is JsonObject criteria)
            criteria.Remove("cursor");

        return JsonSerializer.SerializeToElement(projected, Json);
    }
}
