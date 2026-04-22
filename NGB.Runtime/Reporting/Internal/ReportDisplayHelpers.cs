using NGB.Core.Dimensions;

namespace NGB.Runtime.Reporting.Internal;

internal static class ReportDisplayHelpers
{
    public static string ShortGuid(Guid value)
    {
        var s = value.ToString("N");
        return s.Length > 8 ? s[..8] : s;
    }

    public static IReadOnlyList<string> ToDimensionDisplayValues(
        this DimensionBag bag,
        IReadOnlyDictionary<Guid, string>? displays)
    {
        if (bag.IsEmpty)
            return [];

        var result = new List<string>(bag.Count);

        foreach (var item in bag)
        {
            if (displays is not null
                && displays.TryGetValue(item.DimensionId, out var display)
                && !string.IsNullOrWhiteSpace(display))
            {
                result.Add(display.Trim());
            }
            else
            {
                result.Add(ShortGuid(item.ValueId));
            }
        }

        return result;
    }

    public static string BuildDimensionSetDisplay(
        this DimensionBag bag,
        IReadOnlyDictionary<Guid, string>? displays)
    {
        var values = bag.ToDimensionDisplayValues(displays);
        return values.Count == 0 ? "—" : string.Join(" · ", values);
    }

    public static string BuildAccountDisplay(string code, string? name)
    {
        var trimmedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        var trimmedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

        if (trimmedCode is null && trimmedName is null)
            return "—";

        if (trimmedCode is null)
            return trimmedName!;

        if (trimmedName is null)
            return trimmedCode;

        return $"{trimmedCode} — {trimmedName}";
    }
}
