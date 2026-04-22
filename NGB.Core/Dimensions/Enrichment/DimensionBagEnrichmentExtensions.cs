using NGB.Tools.Exceptions;

namespace NGB.Core.Dimensions.Enrichment;

/// <summary>
/// Shared helpers for mapping <see cref="DimensionBag"/> values to enriched display strings.
/// </summary>
public static class DimensionBagEnrichmentExtensions
{
    /// <summary>
    /// Collects unique enrichment keys from the supplied dimension bags.
    /// </summary>
    public static IReadOnlyList<DimensionValueKey> CollectValueKeys(this IEnumerable<DimensionBag> bags)
    {
        if (bags is null)
            throw new NgbArgumentRequiredException(nameof(bags));

        var set = new HashSet<DimensionValueKey>();

        foreach (var bag in bags)
        {
            if (bag.IsEmpty)
                continue;

            foreach (var x in bag)
            {
                set.Add(new DimensionValueKey(x.DimensionId, x.ValueId));
            }
        }

        return set.Count == 0 ? [] : set.ToArray();
    }

    /// <summary>
    /// Builds a dictionary (DimensionId -&gt; display string) for a bag using resolved enrichment values.
    /// Falls back to short ValueId if a mapping is missing.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> ToValueDisplayMap(
        this DimensionBag bag,
        IReadOnlyDictionary<DimensionValueKey, string> resolved)
    {
        if (bag is null)
            throw new NgbArgumentRequiredException(nameof(bag));

        if (resolved is null)
            throw new NgbArgumentRequiredException(nameof(resolved));

        if (bag.IsEmpty)
            return new Dictionary<Guid, string>();

        var result = new Dictionary<Guid, string>(capacity: bag.Count);

        foreach (var x in bag)
        {
            var key = new DimensionValueKey(x.DimensionId, x.ValueId);
            if (resolved.TryGetValue(key, out var display) && !string.IsNullOrWhiteSpace(display))
            {
                result[x.DimensionId] = display;
            }
            else
            {
                // Short GUID keeps UI compact but is still deterministic.
                var s = x.ValueId.ToString("N");
                result[x.DimensionId] = s.Length > 8 ? s[..8] : s;
            }
        }

        return result;
    }
}
