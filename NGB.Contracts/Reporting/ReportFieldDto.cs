using NGB.Contracts.Metadata;

namespace NGB.Contracts.Reporting;

public sealed record ReportFieldDto(
    string Code,
    string Label,
    string DataType,
    ReportFieldKind Kind,
    bool IsFilterable = false,
    bool IsGroupable = false,
    bool IsSortable = false,
    bool IsSelectable = false,
    bool SupportsIncludeDescendants = false,
    bool DefaultIncludeDescendants = false,
    IReadOnlyList<ReportTimeGrain>? SupportedTimeGrains = null,
    LookupSourceDto? Lookup = null,
    string? Description = null,
    string? Format = null);
