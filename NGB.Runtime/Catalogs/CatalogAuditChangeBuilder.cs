using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;

namespace NGB.Runtime.Catalogs;

internal static class CatalogAuditChangeBuilder
{
    public static IReadOnlyList<AuditFieldChange> BuildCreateChanges(CatalogItemDto item, string catalogCode)
    {
        var changes = new List<AuditFieldChange>
        {
            new("catalog_code", null, JsonSerializer.Serialize(catalogCode)),
            new("is_deleted", null, "false")
        };

        changes.AddRange(Flatten(item.Payload).Select(x => new AuditFieldChange(x.Path, null, x.Json)));
        return changes.ToArray();
    }

    public static IReadOnlyList<AuditFieldChange> BuildUpdateChanges(CatalogItemDto before, CatalogItemDto after)
    {
        var oldMap = Flatten(before.Payload).ToDictionary(x => x.Path, x => x.Json, StringComparer.OrdinalIgnoreCase);
        var newMap = Flatten(after.Payload).ToDictionary(x => x.Path, x => x.Json, StringComparer.OrdinalIgnoreCase);

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

    private static IEnumerable<(string Path, string Json)> Flatten(RecordPayload payload)
    {
        var fields = payload.Fields ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in fields.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (kv.Value.ValueKind == JsonValueKind.Undefined)
                continue;
            yield return (kv.Key, kv.Value.GetRawText());
        }

        var parts = payload.Parts ?? new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase);
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

    private static bool JsonEquals(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.Ordinal);
}
