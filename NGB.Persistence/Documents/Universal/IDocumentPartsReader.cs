using NGB.Metadata.Documents.Hybrid;

namespace NGB.Persistence.Documents.Universal;

/// <summary>
/// Universal reader for document tabular parts (doc_*__*).
///
/// Returned rows are raw DB values (object?) keyed by column name.
/// The Runtime layer converts values to <see cref="System.Text.Json.JsonElement"/>.
/// </summary>
public interface IDocumentPartsReader
{
    /// <summary>
    /// Reads all part rows for the given document id.
    ///
    /// The dictionary key is the physical table name (<c>doc_x__lines</c> etc.).
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>> GetPartsAsync(
        IReadOnlyList<DocumentTableMetadata> partTables,
        Guid documentId,
        CancellationToken ct = default);
}
