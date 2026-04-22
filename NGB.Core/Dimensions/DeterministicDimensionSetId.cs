using System.Text;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Core.Dimensions;

/// <summary>
/// Deterministically computes DimensionSetId from a canonical <see cref="DimensionBag"/>.
/// 
/// IMPORTANT:
/// - This does NOT persist the set. Use <c>IDimensionSetService.GetOrCreateIdAsync</c> when you need persistence.
/// - Canonical representation is expected to be sorted/deduped (DimensionBag guarantees this).
/// </summary>
public static class DeterministicDimensionSetId
{
    public static Guid FromBag(DimensionBag bag)
    {
        if (bag is null)
            throw new NgbArgumentRequiredException(nameof(bag));

        return bag.IsEmpty
            ? Guid.Empty
            : FromCanonicalItems(bag.Items);
    }

    /// <summary>
    /// Computes DimensionSetId from canonical (already sorted &amp; deduped) items.
    /// </summary>
    public static Guid FromCanonicalItems(IReadOnlyList<DimensionValue> items)
    {
        if (items.Count == 0)
            return Guid.Empty;

        // Must be culture-invariant and stable.
        // Format: dimId=valueId;dimId=valueId;... (both as N without hyphens)
        var sb = new StringBuilder(items.Count * 80);

        for (var i = 0; i < items.Count; i++)
        {
            var x = items[i];

            if (i > 0)
                sb.Append(';');

            sb.Append(x.DimensionId.ToString("N"));
            sb.Append('=');
            sb.Append(x.ValueId.ToString("N"));
        }

        return DeterministicGuid.Create($"DimensionSet|{sb}");
    }
}
