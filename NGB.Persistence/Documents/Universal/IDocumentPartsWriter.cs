using NGB.Metadata.Documents.Hybrid;

namespace NGB.Persistence.Documents.Universal;

/// <summary>
/// Universal writer for document tabular parts (doc_*__*).
///
/// Semantics: replace-by-document for drafts.
/// Implementations are expected to DELETE all existing rows for the document
/// and INSERT the supplied rows within the same transaction.
/// </summary>
public interface IDocumentPartsWriter
{
    /// <summary>
    /// Replaces rows in the specified part tables for the given document id.
    ///
    /// <paramref name="rowsByTable"/> keys are physical table names.
    /// Missing table keys are treated as an empty rows list.
    /// </summary>
    Task ReplacePartsAsync(
        IReadOnlyList<DocumentTableMetadata> partTables,
        Guid documentId,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> rowsByTable,
        CancellationToken ct = default);
}
