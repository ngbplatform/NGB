using NGB.Core.Dimensions;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Readers;

/// <summary>
/// Normalizes dimension filters for SQL readers.
/// </summary>
internal static class SqlDimensionFilter
{
    public static (Guid[] DimIds, Guid[] DimValueIds, int DimCount) Normalize(
        IReadOnlyList<DimensionValue>? dimensions)
    {
        if (dimensions is null || dimensions.Count == 0)
            return ([], [], 0);

        var dimIds = new Guid[dimensions.Count];
        var valueIds = new Guid[dimensions.Count];
        var seen = new HashSet<Guid>();

        for (var i = 0; i < dimensions.Count; i++)
        {
            var d = dimensions[i];
            if (!seen.Add(d.DimensionId))
                throw new NgbArgumentInvalidException(nameof(dimensions), $"Dimension filter contains a duplicate dimension id: {d.DimensionId}");

            dimIds[i] = d.DimensionId;
            valueIds[i] = d.ValueId;
        }

        return (dimIds, valueIds, dimensions.Count);
    }

    public static (Guid[] ScopeDimIds, Guid[] ScopeValueIds, int ScopeDimensionCount) NormalizeScopes(
        DimensionScopeBag? scopes)
    {
        if (scopes is null || scopes.IsEmpty)
            return ([], [], 0);

        var pairCount = scopes.Sum(x => x.ValueIds.Count);
        var dimIds = new Guid[pairCount];
        var valueIds = new Guid[pairCount];
        var offset = 0;

        foreach (var scope in scopes)
        {
            foreach (var valueId in scope.ValueIds)
            {
                dimIds[offset] = scope.DimensionId;
                valueIds[offset] = valueId;
                offset++;
            }
        }

        return (dimIds, valueIds, scopes.Count);
    }
}
