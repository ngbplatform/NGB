using NGB.Accounting.Reports.StatementOfChangesInEquity;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Internal;

namespace NGB.Runtime.Reporting.Canonical;

public sealed class StatementOfChangesInEquityCanonicalReportExecutor(IStatementOfChangesInEquityReportReader reader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => "accounting.statement_of_changes_in_equity";

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var (rawFrom, rawTo, from, to) = CanonicalReportExecutionHelper.GetRequiredDateRange(definition, request);

        var report = await reader.GetAsync(
            new StatementOfChangesInEquityReportRequest
            {
                FromInclusive = from,
                ToInclusive = to
            },
            ct);

        var rows = report.Lines.Select(line => ToDetailRow(line, rawFrom, rawTo, request.Filters)).ToList();

        if (request.Layout?.ShowGrandTotals != false)
        {
            rows.Add(new ReportSheetRowDto(
                ReportRowKind.Total,
                Cells:
                [
                    new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Total Equity"), "Total Equity", "string", SemanticRole: "label"),
                    new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(report.TotalOpening), report.TotalOpening.ToString("0.##"), "decimal", SemanticRole: "total"),
                    new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(report.TotalChange), report.TotalChange.ToString("0.##"), "decimal", SemanticRole: "total"),
                    new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(report.TotalClosing), report.TotalClosing.ToString("0.##"), "decimal", SemanticRole: "total")
                ],
                SemanticRole: "grand_total"));
        }

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("component", "Component", "string", Width: 360, IsFrozen: true),
                new ReportSheetColumnDto("opening", "Opening", "decimal", Width: 140),
                new ReportSheetColumnDto("change", "Change", "decimal", Width: 140),
                new ReportSheetColumnDto("closing", "Closing", "decimal", Width: 140)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"{rawFrom:yyyy-MM-dd} → {rawTo:yyyy-MM-dd}",
                HasRowOutline: false,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-statement-of-changes-in-equity"
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: 0,
            limit: rows.Count,
            total: rows.Count,
            hasMore: false,
            nextCursor: null,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-statement-of-changes-in-equity"
            });
    }

    private static ReportSheetRowDto ToDetailRow(
        StatementOfChangesInEquityLine line,
        DateOnly rawFrom,
        DateOnly rawTo,
        IReadOnlyDictionary<string, ReportFilterValueDto>? inheritedFilters)
    {
        var display = line.IsSynthetic
            ? line.ComponentName
            : ReportDisplayHelpers.BuildAccountDisplay(line.ComponentCode, line.ComponentName);

        return new ReportSheetRowDto(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(display),
                    display,
                    "string",
                    Action: line.IsSynthetic || line.AccountId == Guid.Empty
                        ? null
                        : ReportCellActions.BuildAccountCardAction(line.AccountId, rawFrom, rawTo, inheritedFilters)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.OpeningAmount), line.OpeningAmount.ToString("0.##"), "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.ChangeAmount), line.ChangeAmount.ToString("0.##"), "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.ClosingAmount), line.ClosingAmount.ToString("0.##"), "decimal")
            ],
            GroupKey: line.ComponentCode);
    }
}
