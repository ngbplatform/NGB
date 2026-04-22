using NGB.Core.Dimensions;

namespace NGB.Runtime.Dimensions;

/// <summary>
/// Resolves a canonical <see cref="Guid"/> DimensionSetId for an arbitrary set of dimensions.
///
/// The ID is deterministic by the dimension bag content, and the mapping is persisted for reporting.
/// </summary>
public interface IDimensionSetService
{
    /// <summary>
    /// Returns Guid.Empty for <see cref="DimensionBag.Empty"/>.
    ///
    /// For non-empty bags, this method requires an active UnitOfWork transaction
    /// (the persisted mapping must be part of the caller's business transaction).
    /// </summary>
    Task<Guid> GetOrCreateIdAsync(DimensionBag bag, CancellationToken ct = default);

    /// <summary>
    /// Resolves and persists multiple dimension bags in one transactional batch.
    ///
    /// Result order matches <paramref name="bags"/> order.
    /// Empty bags resolve to <see cref="Guid.Empty"/>.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetOrCreateIdsAsync(IReadOnlyList<DimensionBag> bags, CancellationToken ct = default);
}
