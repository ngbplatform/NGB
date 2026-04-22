using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.PostgreSql.Reporting;

public sealed record PostgresReportExecutionRequest(
    string DatasetCode,
    IReadOnlyList<PostgresReportGroupingSelection> RowGroups,
    IReadOnlyList<PostgresReportGroupingSelection> ColumnGroups,
    IReadOnlyList<PostgresReportFieldSelection> DetailFields,
    IReadOnlyList<PostgresReportMeasureSelection> Measures,
    IReadOnlyList<PostgresReportSortSelection> Sorts,
    IReadOnlyList<PostgresReportPredicateSelection> Predicates,
    IReadOnlyDictionary<string, object?> Parameters,
    PostgresReportPaging Paging)
{
    public string DatasetCodeNorm { get; } = CodeNormalizer.NormalizeCodeNorm(
        string.IsNullOrWhiteSpace(DatasetCode) ? throw new NgbArgumentRequiredException(nameof(DatasetCode)) : DatasetCode,
        nameof(DatasetCode));
}

public sealed record PostgresReportGroupingSelection(
    string FieldCode,
    string OutputCode,
    string Label,
    string DataType,
    ReportTimeGrain? TimeGrain = null,
    bool IncludeDetails = false,
    bool IncludeEmpty = false,
    bool IncludeDescendants = false,
    string? GroupKey = null);

public sealed record PostgresReportFieldSelection(
    string FieldCode,
    string OutputCode,
    string Label,
    string DataType);

public sealed record PostgresReportMeasureSelection(
    string MeasureCode,
    string OutputCode,
    string Label,
    string DataType,
    ReportAggregationKind Aggregation,
    string? FormatOverride = null);

public sealed record PostgresReportSortSelection(
    string FieldCode,
    string? MeasureCode,
    ReportSortDirection Direction,
    ReportTimeGrain? TimeGrain = null,
    bool AppliesToColumnAxis = false,
    string? GroupKey = null);

public sealed record PostgresReportPredicateSelection(
    string FieldCode,
    string OutputCode,
    string Label,
    string DataType,
    ReportFilterValueDto Filter);

public sealed record PostgresReportPaging(int Offset, int Limit, string? Cursor = null, bool DisablePaging = false);
