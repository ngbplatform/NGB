using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Reporting;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;

namespace NGB.PropertyManagement.Runtime.Reporting;

public sealed class OccupancySummaryCanonicalReportExecutor(IOccupancySummaryReader reader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => "pm.occupancy.summary";

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var buildingId = CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, request, "building_id");
        var asOf = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "as_of_utc")
            ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var offset = Math.Max(0, request.Offset);
        var limit = request.Limit <= 0 ? 50 : request.Limit;

        var page = await reader.GetPageAsync(buildingId, asOf, offset, limit, ct);
        page.EnsureInvariant();

        var rows = page.Rows.Select(ToDetailRow).ToList();
        if (request.Layout?.ShowGrandTotals != false && page.Total > 0)
            rows.Add(ToTotalRow(page.Totals));

        var subtitle = buildingId is null
            ? $"Portfolio occupancy · {asOf:yyyy-MM-dd}"
            : $"Occupancy · {asOf:yyyy-MM-dd}";

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("building", "Building", "string", Width: 240, IsFrozen: true),
                new ReportSheetColumnDto("as_of_utc", "As Of", "date", Width: 120),
                new ReportSheetColumnDto("total_units", "Total Units", "int32", Width: 120),
                new ReportSheetColumnDto("occupied_units", "Occupied Units", "int32", Width: 140),
                new ReportSheetColumnDto("vacant_units", "Vacant Units", "int32", Width: 130),
                new ReportSheetColumnDto("occupancy_percent", "Occupancy %", "decimal", Width: 130)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: subtitle,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-pm-occupancy-summary"
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: offset,
            limit: limit,
            total: page.Total,
            hasMore: offset + page.Rows.Count < page.Total,
            nextCursor: null,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-pm-occupancy-summary"
            });
    }

    private static ReportSheetRowDto ToDetailRow(OccupancySummaryRow row)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.BuildingDisplay), row.BuildingDisplay, "string", Action: ReportCellActions.BuildCatalogAction("pm.property", row.BuildingId)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.AsOfUtc), row.AsOfUtc.ToString("yyyy-MM-dd"), "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.TotalUnits), row.TotalUnits.ToString(), "int32"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.OccupiedUnits), row.OccupiedUnits.ToString(), "int32"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.VacantUnits), row.VacantUnits.ToString(), "int32"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.OccupancyPercent), row.OccupancyPercent.ToString("0.##"), "decimal")
            ]);

    private static ReportSheetRowDto ToTotalRow(OccupancySummaryTotals totals)
        => new(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Total"), "Total", "string", SemanticRole: "label"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(totals.AsOfUtc), totals.AsOfUtc.ToString("yyyy-MM-dd"), "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(totals.TotalUnits), totals.TotalUnits.ToString(), "int32", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(totals.OccupiedUnits), totals.OccupiedUnits.ToString(), "int32", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(totals.VacantUnits), totals.VacantUnits.ToString(), "int32", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(totals.OccupancyPercent), totals.OccupancyPercent.ToString("0.##"), "decimal", SemanticRole: "total")
            ],
            SemanticRole: "grand_total");
}
