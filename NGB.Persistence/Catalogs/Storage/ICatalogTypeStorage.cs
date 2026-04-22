namespace NGB.Persistence.Catalogs.Storage;

/// <summary>
/// Per-type catalog storage (tables: cat_{catalog_code}, cat_{catalog_code}__{part}, ...).
///
/// Implementations are provided by an industry solution module.
/// All methods are expected to be called within an active transaction.
/// </summary>
public interface ICatalogTypeStorage
{
    string CatalogCode { get; }

    /// <summary>
    /// Ensures the per-type storage exists for the given catalogId.
    /// Usually inserts into the head table and initializes parts.
    /// </summary>
    Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default);

    /// <summary>
    /// Deletes/clears per-type storage for the given catalogId.
    /// The registry row in 'catalogs' is managed by ICatalogRepository.
    /// </summary>
    Task DeleteAsync(Guid catalogId, CancellationToken ct = default);
}