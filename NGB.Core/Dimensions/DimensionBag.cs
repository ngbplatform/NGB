using System.Collections;
using NGB.Tools.Exceptions;

namespace NGB.Core.Dimensions;

/// <summary>
/// Immutable, canonicalized set of <see cref="DimensionValue"/> items.
///
/// Invariants:
/// - Each DimensionId appears at most once.
/// - Items are sorted by DimensionId (and ValueId for stability).
///
/// This object is the basis for deterministic DimensionSetId calculation.
/// </summary>
public sealed class DimensionBag : IReadOnlyList<DimensionValue>
{
    private readonly DimensionValue[] _items;

    public static DimensionBag Empty { get; } = new([], skipNormalize: true);

    public bool IsEmpty => _items.Length == 0;

    public int Count => _items.Length;

    public DimensionValue this[int index] => _items[index];

    /// <summary>
    /// Canonical items (sorted, de-duplicated). Safe to use for deterministic hashing.
    /// </summary>
    public IReadOnlyList<DimensionValue> Items => _items;

    public DimensionBag(IEnumerable<DimensionValue> items)
    {
        if (items is null)
            throw new NgbArgumentRequiredException(nameof(items));

        var map = new Dictionary<Guid, Guid>();

        foreach (var x in items)
        {
            if (x.DimensionId == Guid.Empty)
                throw new NgbArgumentInvalidException(nameof(items), "DimensionId must not be empty.");

            if (x.ValueId == Guid.Empty)
                throw new NgbArgumentInvalidException(nameof(items), "ValueId must not be empty.");

            if (map.TryGetValue(x.DimensionId, out var existing))
            {
                if (existing != x.ValueId)
                    throw new NgbArgumentInvalidException(nameof(items), $"Duplicate DimensionId with different ValueId: {x.DimensionId}");

                // exact duplicate -> ignore
                continue;
            }

            map.Add(x.DimensionId, x.ValueId);
        }

        if (map.Count == 0)
        {
            _items = [];
            return;
        }

        _items = map.Select(kvp => new DimensionValue(kvp.Key, kvp.Value)).ToArray();

        Array.Sort(_items, static (a, b) =>
        {
            var c = a.DimensionId.CompareTo(b.DimensionId);
            return c != 0 ? c : a.ValueId.CompareTo(b.ValueId);
        });
    }

    private DimensionBag(DimensionValue[] items, bool skipNormalize)
    {
        _items = items;

        if (!skipNormalize)
            throw new NgbInvariantViolationException("Internal-only constructor must be called with skipNormalize=true.");
    }

    public IEnumerator<DimensionValue> GetEnumerator() => ((IEnumerable<DimensionValue>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
