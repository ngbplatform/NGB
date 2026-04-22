using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.Metadata.Documents.Hybrid;

namespace NGB.Runtime.Documents;

internal static class DocumentAuditChangeBuilder
{
    public static IReadOnlyList<AuditFieldChange> BuildCreateChanges(DocumentDto item)
        => BuildCreateChanges(item.Payload, null);

    public static IReadOnlyList<AuditFieldChange> BuildCreateChanges(
        DocumentDto item,
        DocumentPresentationMetadata? presentation)
        => BuildCreateChanges(item.Payload, presentation);

    public static IReadOnlyList<AuditFieldChange> BuildCreateChanges(RecordPayload payload)
        => BuildCreateChanges(payload, null);

    public static IReadOnlyList<AuditFieldChange> BuildCreateChanges(
        RecordPayload payload,
        DocumentPresentationMetadata? presentation)
        => Flatten(payload, presentation).Select(x => new AuditFieldChange(x.Path, null, x.Json)).ToArray();

    public static IReadOnlyList<AuditFieldChange> BuildUpdateChanges(DocumentDto before, DocumentDto after)
        => BuildUpdateChanges(before.Payload, after.Payload, null);

    public static IReadOnlyList<AuditFieldChange> BuildUpdateChanges(
        DocumentDto before,
        DocumentDto after,
        DocumentPresentationMetadata? presentation)
        => BuildUpdateChanges(before.Payload, after.Payload, presentation);

    public static IReadOnlyList<AuditFieldChange> BuildUpdateChanges(RecordPayload before, RecordPayload after)
        => BuildUpdateChanges(before, after, null);

    public static IReadOnlyList<AuditFieldChange> BuildUpdateChanges(
        RecordPayload before,
        RecordPayload after,
        DocumentPresentationMetadata? presentation)
    {
        var oldMap = Flatten(before, presentation).ToDictionary(x => x.Path, x => x.Json, StringComparer.OrdinalIgnoreCase);
        var newMap = Flatten(after, presentation).ToDictionary(x => x.Path, x => x.Json, StringComparer.OrdinalIgnoreCase);

        return oldMap.Keys
            .Concat(newMap.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                oldMap.TryGetValue(path, out var oldJson);
                newMap.TryGetValue(path, out var newJson);
                return new AuditFieldChange(path, oldJson, newJson);
            })
            .Where(x => !JsonEquals(x.OldValueJson, x.NewValueJson))
            .ToArray();
    }

    private static HashSet<string> GetIgnoredTopLevelFieldNames(DocumentPresentationMetadata? presentation)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (presentation?.ComputedDisplay == true)
            ignored.Add("display");

        if (presentation?.HasNumber == true)
            ignored.Add("number");

        return ignored;
    }

    private static IEnumerable<(string Path, string Json)> Flatten(
        RecordPayload payload,
        DocumentPresentationMetadata? presentation)
    {
        var ignoredTopLevelFieldNames = GetIgnoredTopLevelFieldNames(presentation);
        var fields = payload.Fields
                     ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var kv in fields.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (ignoredTopLevelFieldNames.Contains(kv.Key))
                continue;

            if (kv.Value.ValueKind == JsonValueKind.Undefined)
                continue;

            yield return (kv.Key, kv.Value.GetRawText());
        }

        var parts = payload.Parts
                    ?? new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var part in parts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var rows = part.Value?.Rows ?? [];
            for (var i = 0; i < rows.Count; i++)
            {
                foreach (var cell in rows[i].OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (cell.Value.ValueKind == JsonValueKind.Undefined)
                        continue;

                    yield return ($"parts.{part.Key}[{i + 1}].{cell.Key}", cell.Value.GetRawText());
                }
            }
        }
    }

    private static bool JsonEquals(string? a, string? b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.Ordinal);
}
