using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Reporting;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;

namespace NGB.PropertyManagement.Runtime.Reporting;

public sealed class BuildingSummaryCanonicalReportExecutor(IBuildingSummaryReader reader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => "pm.building.summary";

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var buildingId = CanonicalReportExecutionHelper.GetRequiredGuidFilter(definition, request, "building_id");
        var asOf = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "as_of_utc")
            ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var summary = await reader.GetSummaryAsync(buildingId, asOf, ct);
        summary.EnsureInvariant();

        var rows = new List<ReportSheetRowDto>
        {
            ToDetailRow(summary, buildingId)
        };

        if (request.Layout?.ShowGrandTotals != false)
            rows.Add(ToTotalRow(summary));

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("building", "Building", "string", Width: 220, IsFrozen: true),
                new ReportSheetColumnDto("as_of_utc", "As Of", "date", Width: 120),
                new ReportSheetColumnDto("total_units", "Total Units", "int32", Width: 120),
                new ReportSheetColumnDto("occupied_units", "Occupied Units", "int32", Width: 140),
                new ReportSheetColumnDto("vacant_units", "Vacant Units", "int32", Width: 130),
                new ReportSheetColumnDto("vacancy_percent", "Vacancy %", "decimal", Width: 120)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"{summary.BuildingDisplay} · {summary.AsOfUtc:yyyy-MM-dd}",
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-pm-building-summary"
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: 0,
            limit: request.Limit,
            total: 1,
            hasMore: false,
            nextCursor: null,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-pm-building-summary"
            });
    }

    private static ReportSheetRowDto ToDetailRow(BuildingSummary summary, Guid buildingId)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(summary.BuildingDisplay), summary.BuildingDisplay, "string", Action: ReportCellActions.BuildCatalogAction("pm.property", buildingId)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(summary.AsOfUtc), summary.AsOfUtc.ToString("yyyy-MM-dd"), "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(summary.TotalUnits), summary.TotalUnits.ToString(), "int32"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(summary.OccupiedUnits), summary.OccupiedUnits.ToString(), "int32"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(summary.VacantUnits), summary.VacantUnits.ToString(), "int32"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(summary.VacancyPercent), summary.VacancyPercent.ToString("0.##"), "decimal")
            ]);

    private static ReportSheetRowDto ToTotalRow(BuildingSummary summary)
        => new(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Total"), "Total", "string", SemanticRole: "label"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(summary.AsOfUtc), summary.AsOfUtc.ToString("yyyy-MM-dd"), "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(summary.TotalUnits), summary.TotalUnits.ToString(), "int32", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(summary.OccupiedUnits), summary.OccupiedUnits.ToString(), "int32", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(summary.VacantUnits), summary.VacantUnits.ToString(), "int32", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(summary.VacancyPercent), summary.VacancyPercent.ToString("0.##"), "decimal", SemanticRole: "total")
            ],
            SemanticRole: "grand_total");
}
