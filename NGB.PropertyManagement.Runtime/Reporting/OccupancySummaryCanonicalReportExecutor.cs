using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Definitions;
using NGB.PropertyManagement.Reporting;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;

namespace NGB.PropertyManagement.Runtime.Reporting;

public sealed class OccupancySummaryCanonicalReportExecutor(IOccupancySummaryReportReader reader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => PropertyManagementSecurityDefaults.OccupancySummaryReport;

    public ReportExecutionRequestDto PrepareExecution(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        DateTimeOffset utcNow)
    {
        var parameters = new Dictionary<string, string>(request.Parameters ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var date = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "as_of_utc")
            ?? DateOnly.FromDateTime(utcNow.UtcDateTime);
        parameters["as_of_utc"] = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        return request with { Parameters = parameters };
    }

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var buildingId = CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, request, "building_id");
        var asOf = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "as_of_utc")
            ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var cursorKind = SpecializedReportCursorCodec.BuildKind(
            ReportCode,
            buildingId?.ToString("D"),
            asOf.ToString("yyyy-MM-dd"));
        var cursor = request.DisablePaging || string.IsNullOrWhiteSpace(request.Cursor)
            ? null
            : SpecializedReportCursorCodec.Decode<OccupancySummaryContinuation>(cursorKind, request.Cursor);
        var limit = request.DisablePaging ? PagingLimits.MaxMaterializedRows + 1 : request.Limit <= 0 ? 50 : request.Limit;
        var page = await reader.GetSliceAsync(buildingId, asOf, cursor, limit, ct);
        var totals = !page.HasMore && request.Layout?.ShowGrandTotals != false
            ? await reader.GetTotalsAsync(buildingId, asOf, ct)
            : null;
        var sheet = CreateSheet(definition, buildingId, asOf, page.Rows, totals);

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet,
            cursor?.Offset ?? 0,
            limit,
            totals?.BuildingCount,
            page.HasMore,
            page.Next is null ? null : SpecializedReportCursorCodec.Encode(cursorKind, page.Next),
            new Dictionary<string, string> { ["executor"] = "canonical-pm-occupancy-summary", ["paging"] = "query" });
    }

    internal static ReportSheetDto CreateSheet(
        ReportDefinitionDto definition,
        Guid? buildingId,
        DateOnly asOf,
        IReadOnlyList<OccupancySummaryRow> data,
        OccupancySummaryTotals? totals)
    {
        var rows = data.Select(ToDetailRow).ToList();
        if (totals is { BuildingCount: > 0 })
            rows.Add(ToTotalRow(totals));

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

        return sheet;
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

    internal static ReportSheetRowDto ToTotalRow(OccupancySummaryTotals totals)
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
