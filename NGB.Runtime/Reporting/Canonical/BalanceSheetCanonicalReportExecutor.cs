using NGB.Accounting.Reports.BalanceSheet;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Internal;

namespace NGB.Runtime.Reporting.Canonical;

public sealed class BalanceSheetCanonicalReportExecutor(IBalanceSheetReportReader reader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => "accounting.balance_sheet";

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var rawAsOf = CanonicalReportExecutionHelper.GetRequiredDateOnlyParameter(definition, request, "as_of_utc");
        var asOf = CanonicalReportExecutionHelper.NormalizeToPeriodMonth(rawAsOf);
        var drilldownFrom = CanonicalReportExecutionHelper.NormalizeToPeriodMonth(rawAsOf);

        var report = await reader.GetAsync(
            new BalanceSheetReportRequest
            {
                AsOfPeriod = asOf,
                DimensionScopes = CanonicalReportExecutionHelper.BuildDimensionScopes(definition, request),
                IncludeZeroAccounts = false,
                IncludeNetIncomeInEquity = true
            },
            ct);

        var rows = new List<ReportSheetRowDto>();
        foreach (var section in report.Sections)
        {
            rows.Add(new ReportSheetRowDto(
                ReportRowKind.Group,
                Cells:
                [
                    new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(section.Title), section.Title, "string"),
                    EmptyAmountCell()
                ],
                GroupKey: section.Section.ToString(),
                SemanticRole: "section"));

            rows.AddRange(section.Lines.Select(line => ToDetailRow(line, drilldownFrom, rawAsOf, request.Filters)));

            if (request.Layout?.ShowSubtotals != false)
            {
                rows.Add(new ReportSheetRowDto(
                    ReportRowKind.Subtotal,
                    Cells:
                    [
                        new ReportCellDto(CanonicalReportExecutionHelper.JsonValue($"{section.Title} total"), $"{section.Title} total", "string", SemanticRole: "label"),
                        new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(section.Total), section.Total.ToString("0.##"), "decimal", SemanticRole: "subtotal")
                    ],
                    SemanticRole: "section_total"));
            }
        }

        if (request.Layout?.ShowGrandTotals != false)
            rows.AddRange(ToGrandTotalRows(report));

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("account", "Account", "string", Width: 360, IsFrozen: true),
                new ReportSheetColumnDto("amount", "Amount", "decimal", Width: 140)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"As of {rawAsOf:yyyy-MM-dd}",
                HasRowOutline: true,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-balance-sheet"
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
                ["executor"] = "canonical-balance-sheet"
            });
    }

    private static ReportSheetRowDto ToDetailRow(
        BalanceSheetLine line,
        DateOnly rawFrom,
        DateOnly rawTo,
        IReadOnlyDictionary<string, ReportFilterValueDto>? inheritedFilters)
    {
        var accountDisplay = ReportDisplayHelpers.BuildAccountDisplay(line.AccountCode, line.AccountName);
        return new ReportSheetRowDto(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(
                    CanonicalReportExecutionHelper.JsonValue(accountDisplay),
                    accountDisplay,
                    "string",
                    Action: ReportCellActions.BuildAccountCardAction(line.AccountId, rawFrom, rawTo, inheritedFilters)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.Amount), line.Amount.ToString("0.##"), "decimal")
            ],
            OutlineLevel: 1,
            GroupKey: $"detail:{line.AccountId}");
    }

    private static IReadOnlyList<ReportSheetRowDto> ToGrandTotalRows(BalanceSheetReport report)
        =>
        [
            TotalRow("Total Assets", report.TotalAssets),
            TotalRow("Total Liabilities", report.TotalLiabilities),
            TotalRow("Total Equity", report.TotalEquity),
            TotalRow("Total Liabilities + Equity", report.TotalLiabilitiesAndEquity),
            TotalRow("Difference", report.Difference),
            new ReportSheetRowDto(
                ReportRowKind.Total,
                Cells:
                [
                    new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Balanced"), "Balanced", "string", SemanticRole: "label"),
                    new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(report.IsBalanced), report.IsBalanced ? "Yes" : "No", "bool", SemanticRole: "total")
                ],
                SemanticRole: "grand_total")
        ];

    private static ReportSheetRowDto TotalRow(string label, decimal amount)
        => new(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(label), label, "string", SemanticRole: "label"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(amount), amount.ToString("0.##"), "decimal", SemanticRole: "total")
            ],
            SemanticRole: "grand_total");

    private static ReportCellDto EmptyAmountCell() => new(Value: null, Display: string.Empty, ValueType: "decimal");
}
