using NGB.Accounting.CashFlow;
using NGB.Accounting.Reports.CashFlowIndirect;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Readers.Reports;

namespace NGB.Runtime.Reporting.Canonical;

public sealed class CashFlowIndirectCanonicalReportExecutor(ICashFlowIndirectReportReader reader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => "accounting.cash_flow_statement_indirect";

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var (rawFrom, rawTo, _, _) = CanonicalReportExecutionHelper.GetRequiredDateRange(definition, request);

        var report = await reader.GetAsync(
            new CashFlowIndirectReportRequest
            {
                FromInclusive = rawFrom,
                ToInclusive = rawTo
            },
            ct);

        var rows = new List<ReportSheetRowDto>();
        foreach (var section in report.Sections)
        {
            rows.Add(new ReportSheetRowDto(
                ReportRowKind.Group,
                Cells:
                [
                    new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(section.Label), section.Label, "string", SemanticRole: "label"),
                    new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(section.Total), section.Total.ToString("0.##"), "decimal", SemanticRole: "group_total")
                ],
                GroupKey: section.Section.ToString(),
                SemanticRole: "section"));

            rows.AddRange(section.Lines.Select(line => new ReportSheetRowDto(
                ReportRowKind.Detail,
                Cells:
                [
                    new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.Label), line.Label, "string", SemanticRole: "label"),
                    new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.Amount), line.Amount.ToString("0.##"), "decimal")
                ],
                GroupKey: section.Section.ToString(),
                SemanticRole: line.IsSynthetic ? "synthetic_line" : "line")));

            rows.Add(new ReportSheetRowDto(
                ReportRowKind.Total,
                Cells:
                [
                    new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(GetSectionTotalLabel(section.Section)), GetSectionTotalLabel(section.Section), "string", SemanticRole: "label"),
                    new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(section.Total), section.Total.ToString("0.##"), "decimal", SemanticRole: "total")
                ],
                GroupKey: section.Section.ToString(),
                SemanticRole: "section_total"));
        }

        rows.Add(new ReportSheetRowDto(
            ReportRowKind.Group,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Reconciliation"), "Reconciliation", "string", SemanticRole: "label"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(report.EndingCash), report.EndingCash.ToString("0.##"), "decimal", SemanticRole: "group_total")
            ],
            GroupKey: "Reconciliation",
            SemanticRole: "section"));

        rows.Add(new ReportSheetRowDto(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Cash and cash equivalents at beginning of period"), "Cash and cash equivalents at beginning of period", "string", SemanticRole: "label"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(report.BeginningCash), report.BeginningCash.ToString("0.##"), "decimal")
            ],
            GroupKey: "Reconciliation"));

        rows.Add(new ReportSheetRowDto(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Net increase (decrease) in cash and cash equivalents"), "Net increase (decrease) in cash and cash equivalents", "string", SemanticRole: "label"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(report.NetIncreaseDecreaseInCash), report.NetIncreaseDecreaseInCash.ToString("0.##"), "decimal", SemanticRole: "total")
            ],
            GroupKey: "Reconciliation",
            SemanticRole: "reconciliation_total"));

        rows.Add(new ReportSheetRowDto(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Cash and cash equivalents at end of period"), "Cash and cash equivalents at end of period", "string", SemanticRole: "label"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(report.EndingCash), report.EndingCash.ToString("0.##"), "decimal", SemanticRole: "total")
            ],
            GroupKey: "Reconciliation",
            SemanticRole: "ending_cash"));

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("line", "Line", "string", Width: 420, IsFrozen: true),
                new ReportSheetColumnDto("amount", "Amount", "decimal", Width: 160)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"{rawFrom:yyyy-MM-dd} → {rawTo:yyyy-MM-dd}",
                HasRowOutline: false,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-cash-flow-indirect"
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
                ["executor"] = "canonical-cash-flow-indirect"
            });
    }

    private static string GetSectionTotalLabel(CashFlowSection section)
        => section switch
        {
            CashFlowSection.Operating => "Net cash from operating activities",
            CashFlowSection.Investing => "Net cash from investing activities",
            CashFlowSection.Financing => "Net cash from financing activities",
            _ => "Total"
        };
}
