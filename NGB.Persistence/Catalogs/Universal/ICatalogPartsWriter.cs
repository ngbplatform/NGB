using NGB.Metadata.Catalogs.Hybrid;

namespace NGB.Persistence.Catalogs.Universal;

/// <summary>
/// Universal writer for catalog tabular parts (cat_*__*).
///
/// Semantics: replace-by-catalog for updates that specify a part.
/// Implementations are expected to DELETE all existing rows for the catalog
/// and INSERT the supplied rows within the same transaction.
/// </summary>
public interface ICatalogPartsWriter
{
    /// <summary>
    /// Replaces rows in the specified part tables for the given catalog id.
    ///
    /// <paramref name="rowsByTable"/> keys are physical table names.
    /// Missing table keys are treated as an empty rows list.
    /// </summary>
    Task ReplacePartsAsync(
        IReadOnlyList<CatalogTableMetadata> partTables,
        Guid catalogId,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> rowsByTable,
        CancellationToken ct = default);
}
