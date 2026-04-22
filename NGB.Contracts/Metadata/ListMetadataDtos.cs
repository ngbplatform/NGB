namespace NGB.Contracts.Metadata;

public enum ColumnAlign
{
    Left = 1,
    Center = 2,
    Right = 3
}

public sealed record MetadataOptionDto(string Value, string Label);

public sealed record ListFilterOptionDto(string Value, string Label);

public sealed record ColumnMetadataDto(
    string Key,
    string Label,
    DataType DataType,
    bool IsSortable = true,
    int? WidthPx = null,
    ColumnAlign Align = ColumnAlign.Left,
    LookupSourceDto? Lookup = null,
    IReadOnlyList<MetadataOptionDto>? Options = null);

public sealed record ListFilterFieldDto(
    string Key,
    string Label,
    DataType DataType,
    bool IsMulti = false,
    LookupSourceDto? Lookup = null,
    IReadOnlyList<MetadataOptionDto>? Options = null,
    string? Description = null);

public sealed record ListMetadataDto(
    IReadOnlyList<ColumnMetadataDto> Columns,
    IReadOnlyList<ListFilterFieldDto>? Filters = null);
