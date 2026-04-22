using NGB.Core.Dimensions;

namespace NGB.Persistence.Dimensions;

/// <summary>
/// Persists (DimensionSetId -> items) mapping.
///
/// Contract:
/// - Dimension sets are immutable / append-only.
/// - Writer must be idempotent (safe under retries and concurrency).
/// - Writer must participate in the caller's transaction (no autocommit).
/// </summary>
public interface IDimensionSetWriter
{
    /// <summary>
    /// Ensures the dimension set exists and contains the given items.
    ///
    /// Implementations must be safe under concurrency.
    /// </summary>
    Task EnsureExistsAsync(Guid dimensionSetId, IReadOnlyList<DimensionValue> items, CancellationToken ct = default);

    /// <summary>
    /// Ensures many dimension sets exist in a single transactional batch.
    /// Implementations must be safe under concurrency and preserve immutability guarantees.
    /// </summary>
    Task EnsureExistsBatchAsync(IReadOnlyList<DimensionSetWrite> sets, CancellationToken ct = default);
}

public sealed record DimensionSetWrite(Guid DimensionSetId, IReadOnlyList<DimensionValue> Items);
