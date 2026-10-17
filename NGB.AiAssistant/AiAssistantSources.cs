using System.Text.Json;

namespace NGB.AiAssistant;

internal static class AiAssistantSources
{
    internal const int MaximumSources = 30;

    internal static IEnumerable<AiAssistantSource> Read(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("kind", out var kind)
                && kind.ValueKind == JsonValueKind.String
                && kind.GetString() is "catalog" or "document" or "generalJournalEntry" or "report"
                && value.TryGetProperty("typeCode", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() is { Length: > 0 and <= 128 } typeCode)
            {
                var hasId = value.TryGetProperty("recordId", out var id) || value.TryGetProperty("id", out id);
                Guid? recordId = hasId
                    && id.ValueKind == JsonValueKind.String
                    && id.TryGetGuid(out var parsed) ? parsed : null;

                if (kind.GetString() == "report" || recordId is not null && recordId != Guid.Empty)
                {
                    var title = value.TryGetProperty("title", out var text)
                        && text.ValueKind == JsonValueKind.String ? text.GetString() : typeCode;

                    yield return new(
                        kind.GetString()!,
                        typeCode,
                        recordId, 
                        title is { Length: > 0 and <= 256 } ? title : typeCode);
                }
            }

            if (value.TryGetProperty("reportCode", out var report)
                && report.ValueKind == JsonValueKind.String
                && report.GetString() is { Length: > 0 and <= 128 } reportCode
                && value.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String)
            {
                var title = name.GetString();

                yield return new(
                    "report",
                    reportCode,
                    null,
                    title is { Length: > 0 and <= 256 } ? title : reportCode);
            }

            foreach (var property in value.EnumerateObject())
            {
                foreach (var source in Read(property.Value))
                {
                    yield return source;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                foreach (var source in Read(item))
                {
                    yield return source;
                }
            }
        }
    }
}
