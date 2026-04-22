using NGB.Core.Dimensions;

namespace NGB.Persistence.Dimensions;

/// <summary>
/// Read-side API for resolving <c>DimensionSetId</c> to its canonical <see cref="DimensionBag"/>.
/// </summary>
public interface IDimensionSetReader
{
    /// <summary>
    /// Returns resolved dimension bags for the provided set ids.
    ///
    /// Notes:
    /// - <see cref="Guid.Empty"/> MUST map to <see cref="DimensionBag.Empty"/>.
    /// - Non-empty ids are expected to exist due to FK constraints, but implementations
    ///   should be defensive and return an empty bag if a set has no items.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DimensionBag>> GetBagsByIdsAsync(
        IReadOnlyCollection<Guid> dimensionSetIds,
        CancellationToken ct = default);
}
