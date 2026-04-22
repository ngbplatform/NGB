using NGB.Core.Catalogs;

namespace NGB.Persistence.Catalogs;

/// <summary>
/// Catalog registry repository (table: catalogs).
///
/// IMPORTANT:
/// - This repository stores only the common header.
/// - Per-type data belongs in separate tables:
///   cat_{catalog_code}, cat_{catalog_code}__{part}, ...
/// - For state transitions, use GetForUpdateAsync inside an active transaction.
/// </summary>
public interface ICatalogRepository
{
    Task CreateAsync(CatalogRecord catalog, CancellationToken ct = default);

    Task CreateManyAsync(IReadOnlyList<CatalogRecord> catalogs, CancellationToken ct = default);

    Task<CatalogRecord?> GetAsync(Guid catalogId, CancellationToken ct = default);

    /// <summary>
    /// Loads and locks the catalog row until the current transaction completes (SELECT ... FOR UPDATE).
    /// Requires an active transaction.
    /// </summary>
    Task<CatalogRecord?> GetForUpdateAsync(Guid catalogId, CancellationToken ct = default);

    Task MarkForDeletionAsync(Guid catalogId, DateTime updatedAtUtc, CancellationToken ct = default);

    Task UnmarkForDeletionAsync(Guid catalogId, DateTime updatedAtUtc, CancellationToken ct = default);

    Task TouchAsync(Guid catalogId, DateTime updatedAtUtc, CancellationToken ct = default);
}
