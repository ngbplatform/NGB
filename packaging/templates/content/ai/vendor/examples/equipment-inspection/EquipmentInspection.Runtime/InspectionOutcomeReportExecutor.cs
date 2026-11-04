using System.Globalization;
using System.Text.Json;
using EquipmentInspection.Definitions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;

namespace EquipmentInspection.Runtime;

public sealed class InspectionOutcomeReportExecutor(IReportPageDataSource source) : IReportSpecializedPlanExecutor
{
    public string ReportCode => InspectionReports.OutcomeRate;

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var query = new ReportDataQuery(
            ReportCode: ReportCode,
            DatasetCode: InspectionReports.Summary,
            RowGroups: [],
            ColumnGroups: [],
            DetailFields: [],
            Measures: [new ReportPlanMeasure("inspection_count", "count", "Inspections", "int64", ReportAggregationKind.Count)],
            Sorts: [],
            Predicates: [],
            Parameters: request.Parameters!.Select(pair => new ReportPlanParameter(pair.Key, pair.Value)).ToArray());
        var total = await CountAsync(query, ct);
        var passed = await CountAsync(query with
        {
            Predicates =
            [
                new ReportPlanPredicate("result", "result", "Result", "string",
                    new ReportFilterValueDto(JsonSerializer.SerializeToElement("Passed")))
            ]
        }, ct);
        decimal? percentage = total == 0 ? null : passed * 100m / total;
        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("total", "Inspections", "int64"),
                new ReportSheetColumnDto("passed", "Passed", "int64"),
                new ReportSheetColumnDto("percentage", "Passed (%)", "decimal")
            ],
            Rows:
            [
                new ReportSheetRowDto(ReportRowKind.Total,
                [
                    new ReportCellDto(JsonSerializer.SerializeToElement(total), total.ToString(CultureInfo.InvariantCulture), "int64"),
                    new ReportCellDto(JsonSerializer.SerializeToElement(passed), passed.ToString(CultureInfo.InvariantCulture), "int64"),
                    new ReportCellDto(JsonSerializer.SerializeToElement(percentage),
                        percentage is { } value ? value.ToString("0.##", CultureInfo.InvariantCulture) + "%" : "No inspections", "decimal")
                ])
            ],
            Meta: new ReportSheetMetaDto(Title: definition.Name));
        return new ReportDataPage([], [], 0, 1, 1, false,
            Diagnostics: new Dictionary<string, string> { ["executor"] = "inspection-outcome-rate" },
            PrebuiltSheet: sheet);
    }

    private async Task<long> CountAsync(ReportDataQuery query, CancellationToken ct)
    {
        var page = await source.ReadPageAsync(query, new ReportPlanPaging(0, 1), null, ct);
        return Convert.ToInt64(page.Rows.Single().Values["count"], CultureInfo.InvariantCulture);
    }
}
