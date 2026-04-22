using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Reporting;

public sealed class PostgresReportPlanExecutor(PostgresReportDatasetExecutor executor) : ITabularReportPlanExecutor
{
    private readonly PostgresReportDatasetExecutor _executor = executor ?? throw new NgbConfigurationViolationException("PostgreSQL reporting plan executor requires a dataset executor registration.");

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string reportCode,
        string? datasetCode,
        IReadOnlyList<ReportPlanGrouping> rowGroups,
        IReadOnlyList<ReportPlanGrouping> columnGroups,
        IReadOnlyList<ReportPlanFieldSelection> detailFields,
        IReadOnlyList<ReportPlanMeasure> measures,
        IReadOnlyList<ReportPlanSort> sorts,
        IReadOnlyList<ReportPlanPredicate> predicates,
        IReadOnlyList<ReportPlanParameter> parameters,
        ReportPlanPaging paging,
        CancellationToken ct)
    {
        if (definition is null)
            throw new NgbArgumentRequiredException(nameof(definition));

        var result = await _executor.ExecuteAsync(Map(
                reportCode,
                datasetCode,
                rowGroups,
                columnGroups,
                detailFields,
                measures,
                sorts,
                predicates,
                parameters,
                paging,
                request.DisablePaging),
            ct);

        return new ReportDataPage(
            Columns: result.Columns
                .Select(x => new ReportDataColumn(x.OutputCode, x.Title, x.DataType, x.SemanticRole))
                .ToList(),
            Rows: result.Rows
                .Select(x => new ReportDataRow(x.Values))
                .ToList(),
            Offset: result.Offset,
            Limit: result.Limit,
            Total: result.Total,
            HasMore: result.HasMore,
            NextCursor: result.NextCursor,
            Diagnostics: result.Diagnostics);
    }

    private static PostgresReportExecutionRequest Map(
        string reportCode,
        string? datasetCode,
        IReadOnlyList<ReportPlanGrouping> rowGroups,
        IReadOnlyList<ReportPlanGrouping> columnGroups,
        IReadOnlyList<ReportPlanFieldSelection> detailFields,
        IReadOnlyList<ReportPlanMeasure> measures,
        IReadOnlyList<ReportPlanSort> sorts,
        IReadOnlyList<ReportPlanPredicate> predicates,
        IReadOnlyList<ReportPlanParameter> parameters,
        ReportPlanPaging paging,
        bool disablePaging)
        => new(
            DatasetCode: datasetCode ?? throw new NgbConfigurationViolationException($"Reporting plan for '{reportCode}' does not define a dataset code."),
            RowGroups: rowGroups.Select(x => new PostgresReportGroupingSelection(x.FieldCode, x.OutputCode, x.Label, x.DataType, x.TimeGrain, x.IncludeDetails, x.IncludeEmpty, x.IncludeDescendants, x.GroupKey)).ToList(),
            ColumnGroups: columnGroups.Select(x => new PostgresReportGroupingSelection(x.FieldCode, x.OutputCode, x.Label, x.DataType, x.TimeGrain, x.IncludeDetails, x.IncludeEmpty, x.IncludeDescendants, x.GroupKey)).ToList(),
            DetailFields: detailFields.Select(x => new PostgresReportFieldSelection(x.FieldCode, x.OutputCode, x.Label, x.DataType)).ToList(),
            Measures: measures.Select(x => new PostgresReportMeasureSelection(x.MeasureCode, x.OutputCode, x.Label, x.DataType, x.Aggregation, x.FormatOverride)).ToList(),
            Sorts: sorts.Select(x => new PostgresReportSortSelection(x.FieldCode, x.MeasureCode, x.Direction, x.TimeGrain, x.AppliesToColumnAxis, x.GroupKey)).ToList(),
            Predicates: predicates.Select(x => new PostgresReportPredicateSelection(x.FieldCode, x.OutputCode, x.Label, x.DataType, x.Filter)).ToList(),
            Parameters: MapParameters(reportCode, parameters),
            Paging: new PostgresReportPaging(paging.Offset, paging.Limit, paging.Cursor, disablePaging));

    private static IReadOnlyDictionary<string, object?> MapParameters(
        string reportCode,
        IReadOnlyList<ReportPlanParameter> parameters)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in parameters)
        {
            dict[parameter.ParameterCode] = parameter.ParameterCode switch
            {
                "from_utc" => ParseUtcDate(parameter.ParameterCode, parameter.Value),
                "to_utc" => ParseUtcDate(parameter.ParameterCode, parameter.Value),
                "as_of_utc" => ParseUtcDate(parameter.ParameterCode, parameter.Value),
                _ => parameter.Value
            };
        }

        if (dict.TryGetValue("to_utc", out var toValue) && toValue is DateTime toDate)
            dict["to_utc_exclusive"] = toDate.AddDays(1);

        if (dict.TryGetValue("as_of_utc", out var asOfValue) && asOfValue is DateTime asOfDate)
            dict["as_of_utc_exclusive"] = asOfDate.AddDays(1);

        return dict;
    }

    private static DateTime ParseUtcDate(string code, string value)
    {
        if (!DateOnly.TryParse(value, out var date))
            throw new NgbConfigurationViolationException($"Reporting parameter '{code}' must be a valid date in yyyy-MM-dd format.");

        return DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
    }
}
