using NGB.Metadata.Base;

namespace NGB.Metadata.Documents.Hybrid;

public sealed record DocumentColumnMetadata(
    string ColumnName,
    ColumnType Type,
    bool Required = false,
    int? MaxLength = null,
    string? UiLabel = null,
    LookupSourceMetadata? Lookup = null,
    MirroredDocumentRelationshipMetadata? MirroredRelationship = null,
    IReadOnlyList<FieldOptionMetadata>? Options = null
);
