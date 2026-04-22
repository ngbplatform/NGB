using NGB.Accounting.Reports.AccountingConsistency;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Internal;

namespace NGB.Runtime.Reporting.Canonical;

public sealed class AccountingConsistencyCanonicalReportExecutor(
    IAccountingConsistencyReportReader reader,
    IDimensionSetReader dimensionSetReader,
    IDimensionValueEnrichmentReader dimensionValueEnrichmentReader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => "accounting.consistency";

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var rawPeriod = CanonicalReportExecutionHelper.GetRequiredDateOnlyParameter(definition, request, "period_utc");
        var rawPrevious = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "previous_period_utc");
        var period = CanonicalReportExecutionHelper.NormalizeToPeriodMonth(rawPeriod);
        DateOnly? previous = rawPrevious.HasValue ? CanonicalReportExecutionHelper.NormalizeToPeriodMonth(rawPrevious.Value) : null;

        var report = await reader.RunForPeriodAsync(period, previous, ct);

        var dimensionSetIds = report.Issues
            .Where(x => x.DimensionSetId.HasValue && x.DimensionSetId.Value != Guid.Empty)
            .Select(x => x.DimensionSetId!.Value)
            .Distinct()
            .ToArray();

        var bagsById = dimensionSetIds.Length == 0
            ? new Dictionary<Guid, DimensionBag>()
            : await dimensionSetReader.GetBagsByIdsAsync(dimensionSetIds, ct);

        var valueKeys = bagsById.Values.CollectValueKeys();
        var enriched = valueKeys.Count == 0
            ? new Dictionary<DimensionValueKey, string>()
            : await dimensionValueEnrichmentReader.ResolveAsync(valueKeys, ct);

        var rows = report.Issues.Select(issue => ToDetailRow(issue, bagsById, enriched)).ToList();
        if (request.Layout?.ShowGrandTotals != false)
            rows.AddRange(ToGrandTotalRows(report));

        var subtitle = previous.HasValue
            ? $"{rawPeriod:yyyy-MM-dd} · previous {rawPrevious:yyyy-MM-dd}"
            : rawPeriod.ToString("yyyy-MM-dd");

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("kind", "Kind", "string", Width: 160, IsFrozen: true),
                new ReportSheetColumnDto("period", "Month", "date", Width: 120),
                new ReportSheetColumnDto("previous_period", "Previous month", "date", Width: 120),
                new ReportSheetColumnDto("account_code", "Account", "string", Width: 120),
                new ReportSheetColumnDto("dimension_set", "Dimensions", "string", Width: 220),
                new ReportSheetColumnDto("message", "Message", "string", Width: 420)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: subtitle,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-accounting-consistency"
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
                ["executor"] = "canonical-accounting-consistency"
            });
    }

    private static ReportSheetRowDto ToDetailRow(
        AccountingConsistencyIssue issue,
        IReadOnlyDictionary<Guid, DimensionBag> bagsById,
        IReadOnlyDictionary<DimensionValueKey, string> enriched)
    {
        var dimensionDisplay = issue.DimensionSetId.HasValue
            && issue.DimensionSetId.Value != Guid.Empty
            && bagsById.TryGetValue(issue.DimensionSetId.Value, out var bag)
                ? bag.BuildDimensionSetDisplay(bag.ToValueDisplayMap(enriched))
                : string.Empty;

        return new ReportSheetRowDto(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(issue.Kind.ToString()), issue.Kind.ToString(), "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(issue.Period), issue.Period.ToString("yyyy-MM-dd"), "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(issue.PreviousPeriod), issue.PreviousPeriod?.ToString("yyyy-MM-dd") ?? string.Empty, "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(issue.AccountCode ?? string.Empty), issue.AccountCode ?? string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(dimensionDisplay), dimensionDisplay, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(issue.Message), issue.Message, "string")
            ]);
    }

    private static IReadOnlyList<ReportSheetRowDto> ToGrandTotalRows(AccountingConsistencyReport report)
        =>
        [
            TotalRow("Turnovers vs register", report.TurnoversVsRegisterDiffCount),
            TotalRow("Balance vs turnover", report.BalanceVsTurnoverMismatchCount),
            TotalRow("Balance chain", report.BalanceChainMismatchCount),
            TotalRow("Missing keys", report.MissingKeyCount),
            TotalRow("Issue count", report.Issues.Count)
        ];

    private static ReportSheetRowDto TotalRow(string label, long value)
        => new(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(label), label, "string", ColSpan: 5, SemanticRole: "label"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(value), value.ToString(), "int64", SemanticRole: "total")
            ],
            SemanticRole: "grand_total");
}
