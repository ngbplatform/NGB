using NGB.Accounting.Reports.IncomeStatement;
using NGB.Accounting.Accounts;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Internal;

namespace NGB.Runtime.Reporting.Canonical;

public sealed class IncomeStatementCanonicalReportExecutor(IIncomeStatementReportReader reader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => "accounting.income_statement";

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var (rawFrom, rawTo, from, to) = CanonicalReportExecutionHelper.GetRequiredDateRange(definition, request);

        var report = await reader.GetAsync(
            new IncomeStatementReportRequest
            {
                FromInclusive = from,
                ToInclusive = to,
                DimensionScopes = CanonicalReportExecutionHelper.BuildDimensionScopes(definition, request),
                IncludeZeroLines = false
            },
            ct);

        var rows = new List<ReportSheetRowDto>();
        foreach (var section in report.Sections)
        {
            var title = HumanizeSection(section.Section);
            rows.Add(new ReportSheetRowDto(
                ReportRowKind.Group,
                Cells:
                [
                    new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(title), title, "string"),
                    EmptyAmountCell()
                ],
                GroupKey: section.Section.ToString(),
                SemanticRole: "section"));

            rows.AddRange(section.Lines.Select(line => ToDetailRow(line, rawFrom, rawTo, request.Filters)));

            if (request.Layout?.ShowSubtotals != false)
            {
                rows.Add(new ReportSheetRowDto(
                    ReportRowKind.Subtotal,
                    Cells:
                    [
                        new ReportCellDto(CanonicalReportExecutionHelper.JsonValue($"{title} total"), $"{title} total", "string", SemanticRole: "label"),
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
                Subtitle: $"{rawFrom:yyyy-MM-dd} → {rawTo:yyyy-MM-dd}",
                HasRowOutline: true,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-income-statement"
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
                ["executor"] = "canonical-income-statement"
            });
    }

    private static ReportSheetRowDto ToDetailRow(
        IncomeStatementLine line,
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

    private static IReadOnlyList<ReportSheetRowDto> ToGrandTotalRows(IncomeStatementReport report)
        =>
        [
            TotalRow("Total Income", report.TotalIncome),
            TotalRow("Total Expenses", report.TotalExpenses),
            TotalRow("Total Other Income", report.TotalOtherIncome),
            TotalRow("Total Other Expense", report.TotalOtherExpense),
            TotalRow("Net Income", report.NetIncome)
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

    private static string HumanizeSection(StatementSection section)
        => section switch
        {
            StatementSection.Income => "Income",
            StatementSection.CostOfGoodsSold => "Cost of Goods Sold",
            StatementSection.Expenses => "Expenses",
            StatementSection.OtherIncome => "Other Income",
            StatementSection.OtherExpense => "Other Expense",
            _ => section.ToString()
        };

    private static ReportCellDto EmptyAmountCell() => new(Value: null, Display: string.Empty, ValueType: "decimal");
}
