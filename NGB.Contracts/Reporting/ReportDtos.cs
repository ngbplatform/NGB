using System.Text.Json;
using NGB.Contracts.Metadata;

namespace NGB.Contracts.Reporting;

public sealed record ReportTypeMetadataDto(
    string ReportCode,
    string Name,
    string? Group = null,
    string? Description = null,
    IReadOnlyList<ReportParameterMetadataDto>? Parameters = null,
    IReadOnlyList<ReportColumnDto>? Columns = null,
    ReportFilterMetadataDto? Filters = null);

public sealed record ReportFilterMetadataDto(IReadOnlyList<ReportDimensionFilterFieldDto>? DimensionFields = null);

public sealed record ReportDimensionFilterFieldDto(
    Guid DimensionId,
    string DimensionCode,
    string Label,
    LookupSourceDto? Lookup = null,
    bool IsMulti = false,
    bool SupportsIncludeDescendants = false,
    bool DefaultIncludeDescendants = false);

public sealed record ReportColumnDto(string Code, string Caption, string DataType);

public sealed record ReportPageResponseDto(
    IReadOnlyList<ReportColumnDto> Columns,
    IReadOnlyList<IReadOnlyList<JsonElement>> Rows,
    int Offset,
    int Limit,
    int? Total,
    bool HasMore,
    string? NextCursor,
    IReadOnlyDictionary<string, JsonElement>? Summary = null);
