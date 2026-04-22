using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;

namespace NGB.Runtime.Admin;

internal sealed class MainMenuService(IEnumerable<IMainMenuContributor> contributors) : IMainMenuService
{
    public async Task<MainMenuDto> GetMainMenuAsync(CancellationToken ct)
    {
        var allGroups = new List<MainMenuGroupDto>();

        foreach (var c in contributors)
        {
            var groups = await c.ContributeAsync(ct);
            if (groups is { Count: > 0 })
                allGroups.AddRange(groups);
        }

        if (allGroups.Count == 0)
            return new MainMenuDto([]);

        // Merge by group label (case-insensitive). Items are de-duplicated by (kind, code).
        var merged = new Dictionary<string, MutableGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in allGroups)
        {
            var label = g.Label.Trim();
            if (label.Length == 0)
                continue;

            if (!merged.TryGetValue(label, out var mg))
            {
                mg = new MutableGroup(label, g.Ordinal, g.Icon);
                merged.Add(label, mg);
            }
            else
            {
                mg.Ordinal = Math.Min(mg.Ordinal, g.Ordinal);
                if (string.IsNullOrWhiteSpace(mg.Icon) && !string.IsNullOrWhiteSpace(g.Icon))
                    mg.Icon = g.Icon;
            }

            foreach (var item in g.Items)
            {
                var key = BuildItemKey(item);

                if (!mg.Items.TryGetValue(key, out var existing))
                {
                    mg.Items.Add(key, item);
                    continue;
                }

                // Keep the more "important" item (lower ordinal). If equal — keep the first.
                if (item.Ordinal < existing.Ordinal)
                    mg.Items[key] = item;
            }
        }

        var result = merged.Values
            .Select(g => new MainMenuGroupDto(
                Label: g.Label,
                Items: g.Items.Values
                    .OrderBy(x => x.Ordinal)
                    .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Ordinal: g.Ordinal,
                Icon: g.Icon))
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MainMenuDto(result);
    }

    private static string BuildItemKey(MainMenuItemDto item)
        => $"{item.Kind.Trim().ToLowerInvariant()}|{item.Code.Trim().ToLowerInvariant()}";

    private sealed class MutableGroup(string label, int ordinal, string? icon)
    {
        public string Label { get; } = label;
        public int Ordinal { get; set; } = ordinal;
        public string? Icon { get; set; } = icon;
        public Dictionary<string, MainMenuItemDto> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
