using NGB.Metadata.Catalogs.Hybrid;

namespace NGB.Persistence.Catalogs.Universal;

/// <summary>
/// Universal reader for catalog tabular parts (cat_*__*).
///
/// Returned rows are raw DB values (object?) keyed by column name.
/// The Runtime layer converts values to <see cref="System.Text.Json.JsonElement"/>.
/// </summary>
public interface ICatalogPartsReader
{
    /// <summary>
    /// Reads all part rows for the given catalog id.
    ///
    /// The dictionary key is the physical table name (<c>cat_x__lines</c> etc.).
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>> GetPartsAsync(
        IReadOnlyList<CatalogTableMetadata> partTables,
        Guid catalogId,
        CancellationToken ct = default);
}
