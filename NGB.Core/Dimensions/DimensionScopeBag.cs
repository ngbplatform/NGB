using System.Collections;
using NGB.Tools.Exceptions;

namespace NGB.Core.Dimensions;

/// <summary>
/// Immutable canonicalized set of <see cref="DimensionScope"/> items.
///
/// Invariants:
/// - Each DimensionId appears at most once.
/// - Items are sorted by DimensionId for stable processing.
/// </summary>
public sealed class DimensionScopeBag : IReadOnlyList<DimensionScope>
{
    private readonly DimensionScope[] _items;

    public static DimensionScopeBag Empty { get; } = new([], skipNormalize: true);

    public bool IsEmpty => _items.Length == 0;

    public int Count => _items.Length;

    public DimensionScope this[int index] => _items[index];

    public IReadOnlyList<DimensionScope> Items => _items;

    public DimensionScopeBag(IEnumerable<DimensionScope> items)
    {
        if (items is null)
            throw new NgbArgumentRequiredException(nameof(items));

        var map = new Dictionary<Guid, DimensionScope>();
        foreach (var item in items)
        {
            if (item is null)
                throw new NgbArgumentInvalidException(nameof(items), "Dimension scope item must not be null.");

            if (!map.TryAdd(item.DimensionId, item))
                throw new NgbArgumentInvalidException(nameof(items), $"Duplicate DimensionId: {item.DimensionId}");
        }

        if (map.Count == 0)
        {
            _items = [];
            return;
        }

        _items = map.Values.ToArray();
        Array.Sort(_items, static (a, b) => a.DimensionId.CompareTo(b.DimensionId));
    }

    private DimensionScopeBag(DimensionScope[] items, bool skipNormalize)
    {
        _items = items;

        if (!skipNormalize)
            throw new NgbInvariantViolationException("Internal-only constructor must be called with skipNormalize=true.");
    }

    public IEnumerator<DimensionScope> GetEnumerator() => ((IEnumerable<DimensionScope>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
