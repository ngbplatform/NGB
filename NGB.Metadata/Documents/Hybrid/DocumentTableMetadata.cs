using NGB.Metadata.Base;

namespace NGB.Metadata.Documents.Hybrid;

public sealed record DocumentTableMetadata(
    string TableName,
    TableKind Kind,
    IReadOnlyList<DocumentColumnMetadata> Columns,
    IReadOnlyList<DocumentIndexMetadata>? Indexes = null,
    string? PartCode = null);
