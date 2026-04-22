using NGB.Tools.Exceptions;

namespace NGB.Core.Dimensions;

/// <summary>
/// Multi-value filter for a single dimension.
///
/// Semantics:
/// - OR within <see cref="ValueIds"/> for this dimension.
/// - Higher-level callers combine multiple scopes with AND semantics.
/// - <see cref="IncludeDescendants"/> is a generic flag that verticals may interpret
///   for hierarchical dimensions (for example, Building -> child Units).
/// </summary>
public sealed class DimensionScope
{
    private readonly Guid[] _valueIds;

    public Guid DimensionId { get; }

    public IReadOnlyList<Guid> ValueIds => _valueIds;

    public bool IncludeDescendants { get; }

    public DimensionScope(Guid dimensionId, IEnumerable<Guid> valueIds, bool includeDescendants = false)
    {
        if (dimensionId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(dimensionId), "DimensionId must not be empty.");

        if (valueIds is null)
            throw new NgbArgumentRequiredException(nameof(valueIds));

        var distinct = new SortedSet<Guid>();
        foreach (var valueId in valueIds)
        {
            if (valueId == Guid.Empty)
                throw new NgbArgumentInvalidException(nameof(valueIds), "ValueId must not be empty.");

            distinct.Add(valueId);
        }

        if (distinct.Count == 0)
            throw new NgbArgumentInvalidException(nameof(valueIds), "At least one valueId is required.");

        DimensionId = dimensionId;
        IncludeDescendants = includeDescendants;
        _valueIds = distinct.ToArray();
    }
}
