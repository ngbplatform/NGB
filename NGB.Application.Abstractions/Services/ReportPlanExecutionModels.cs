using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

public sealed record ReportPlanGrouping(
    string FieldCode,
    string OutputCode,
    string Label,
    string DataType,
    ReportTimeGrain? TimeGrain = null,
    bool IsColumnAxis = false,
    bool IncludeDetails = false,
    bool IncludeEmpty = false,
    bool IncludeDescendants = false,
    string? GroupKey = null);

public sealed record ReportPlanFieldSelection(
    string FieldCode,
    string OutputCode,
    string Label,
    string DataType);

public sealed record ReportPlanMeasure(
    string MeasureCode,
    string OutputCode,
    string Label,
    string DataType,
    ReportAggregationKind Aggregation,
    string? FormatOverride = null);

public sealed record ReportPlanSort(
    string FieldCode,
    string? MeasureCode,
    ReportSortDirection Direction,
    ReportTimeGrain? TimeGrain = null,
    bool AppliesToColumnAxis = false,
    string? GroupKey = null);

public sealed record ReportPlanPredicate(
    string FieldCode,
    string OutputCode,
    string Label,
    string DataType,
    ReportFilterValueDto Filter);

public sealed record ReportPlanParameter(string ParameterCode, string Value);

public sealed record ReportPlanPaging(int Offset, int Limit, string? Cursor = null);

public sealed record ReportDataPage(
    IReadOnlyList<ReportDataColumn> Columns,
    IReadOnlyList<ReportDataRow> Rows,
    int Offset,
    int Limit,
    int? Total,
    bool HasMore,
    string? NextCursor = null,
    IReadOnlyDictionary<string, string>? Diagnostics = null,
    ReportSheetDto? PrebuiltSheet = null);

public sealed record ReportDataColumn(
    string Code,
    string Title,
    string DataType,
    string? SemanticRole = null);

public sealed record ReportDataRow(IReadOnlyDictionary<string, object?> Values);

public sealed record ReportExecutionResult(
    ReportSheetDto Sheet,
    int Offset,
    int Limit,
    int? Total,
    bool HasMore,
    string? NextCursor = null,
    IReadOnlyDictionary<string, string>? Diagnostics = null);
