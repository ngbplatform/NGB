using NGB.Accounting.Reports.TrialBalance;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Internal;

namespace NGB.Runtime.Reporting.Canonical;

public sealed class TrialBalanceCanonicalReportExecutor(ITrialBalanceReportReader reader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => "accounting.trial_balance";

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var (rawFrom, rawTo, from, to) = CanonicalReportExecutionHelper.GetRequiredDateRange(definition, request);

        var scopes = CanonicalReportExecutionHelper.BuildDimensionScopes(definition, request);
        var page = await reader.GetPageAsync(
            new TrialBalanceReportPageRequest
            {
                FromInclusive = from,
                ToInclusive = to,
                DimensionScopes = scopes,
                Offset = request.Offset,
                Limit = request.Limit,
                ShowSubtotals = request.Layout?.ShowSubtotals != false
            },
            ct);

        var sheetRows = page.Rows
            .Select(row => ToSheetRow(row, rawFrom, rawTo, request.Filters))
            .ToList();
        if (request.Layout?.ShowGrandTotals != false && page.Total > 0)
            sheetRows.Add(ToTotalRow(page.Totals));

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("account", "Account", "string", Width: 420, IsFrozen: true),
                new ReportSheetColumnDto("debit_amount", "Debit", "decimal", Width: 140),
                new ReportSheetColumnDto("credit_amount", "Credit", "decimal", Width: 140)
            ],
            Rows: sheetRows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"{rawFrom:yyyy-MM-dd} → {rawTo:yyyy-MM-dd}",
                HasRowOutline: true,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-trial-balance"
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: 0,
            limit: sheetRows.Count,
            total: sheetRows.Count,
            hasMore: false,
            nextCursor: null,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-trial-balance"
            });
    }

    private static ReportSheetRowDto ToSheetRow(
        TrialBalanceReportRow row,
        DateOnly rawFrom,
        DateOnly rawTo,
        IReadOnlyDictionary<string, ReportFilterValueDto>? inheritedFilters)
        => row.RowKind switch
        {
            TrialBalanceReportRowKind.Group => new ReportSheetRowDto(
                ReportRowKind.Group,
                Cells:
                [
                    LabelCell(row.AccountDisplay),
                    EmptyNumericCell(),
                    EmptyNumericCell()
                ],
                OutlineLevel: row.OutlineLevel,
                GroupKey: row.GroupKey),
            TrialBalanceReportRowKind.Subtotal => new ReportSheetRowDto(
                ReportRowKind.Subtotal,
                Cells:
                [
                    LabelCell(row.AccountDisplay),
                    NumericCell(row.DebitAmount, semanticRole: "subtotal"),
                    NumericCell(row.CreditAmount, semanticRole: "subtotal")
                ],
                OutlineLevel: row.OutlineLevel,
                GroupKey: row.GroupKey,
                SemanticRole: "subtotal"),
            _ => new ReportSheetRowDto(
                ReportRowKind.Detail,
                Cells:
                [
                    LabelCell(
                        row.AccountDisplay,
                        action: row.AccountId.HasValue
                            ? ReportCellActions.BuildAccountCardAction(row.AccountId.Value, rawFrom, rawTo, inheritedFilters)
                            : null),
                    NumericCell(row.DebitAmount),
                    NumericCell(row.CreditAmount)
                ],
                OutlineLevel: row.OutlineLevel,
                GroupKey: row.GroupKey)
        };

    private static ReportSheetRowDto ToTotalRow(TrialBalanceReportTotals totals)
        => new(
            ReportRowKind.Total,
            Cells:
            [
                LabelCell("Total", semanticRole: "label"),
                NumericCell(totals.DebitAmount, semanticRole: "total"),
                NumericCell(totals.CreditAmount, semanticRole: "total")
            ],
            SemanticRole: "grand_total");

    private static ReportCellDto LabelCell(string text, ReportCellActionDto? action = null, string? semanticRole = null)
        => new(
            CanonicalReportExecutionHelper.JsonValue(text),
            text,
            "string",
            SemanticRole: semanticRole,
            Action: action);

    private static ReportCellDto NumericCell(decimal value, string? semanticRole = null)
        => new(CanonicalReportExecutionHelper.JsonValue(value), value.ToString("0.##"), "decimal", SemanticRole: semanticRole);

    private static ReportCellDto EmptyNumericCell()
        => new(Value: null, Display: string.Empty, ValueType: "decimal");
}
