using NGB.Contracts.Metadata;

namespace NGB.Contracts.Reporting;

public sealed record ReportFilterFieldDto(
    string FieldCode,
    string Label,
    string DataType,
    bool IsRequired = false,
    bool IsMulti = false,
    bool SupportsIncludeDescendants = false,
    bool DefaultIncludeDescendants = false,
    LookupSourceDto? Lookup = null,
    IReadOnlyList<ReportFilterOptionDto>? Options = null,
    string? Description = null);
