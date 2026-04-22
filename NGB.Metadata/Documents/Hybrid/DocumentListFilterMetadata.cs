using NGB.Metadata.Base;

namespace NGB.Metadata.Documents.Hybrid;

public sealed record DocumentListFilterOptionMetadata(string Value, string Label);

public sealed record DocumentListFilterMetadata(
    string Key,
    string Label,
    ColumnType Type,
    bool IsMulti = false,
    string? HeadColumnName = null,
    LookupSourceMetadata? Lookup = null,
    IReadOnlyList<DocumentListFilterOptionMetadata>? Options = null,
    string? Description = null);
