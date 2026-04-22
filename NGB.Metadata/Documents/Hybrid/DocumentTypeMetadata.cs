namespace NGB.Metadata.Documents.Hybrid;

/// <summary>
/// Metadata describing a document type's typed storage tables (hybrid model).
/// The common registry row lives in table: documents.
/// Per-type tables are owned by vertical solutions (e.g., doc_payment, doc_payment__items).
/// Core uses this metadata for schema validation and (later) generic tooling.
/// </summary>
public sealed record DocumentTypeMetadata(
    string TypeCode,
    IReadOnlyList<DocumentTableMetadata> Tables,
    DocumentPresentationMetadata? Presentation = null,
    DocumentMetadataVersion? Version = null,
    IReadOnlyList<DocumentListFilterMetadata>? ListFilters = null);
