using NGB.Accounting.Reports.LedgerAnalysis;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Internal;
using NGB.Tools.Exceptions;
using PlanFieldSelection = NGB.Runtime.Reporting.Planning.ReportPlanFieldSelection;
using PlanGrouping = NGB.Runtime.Reporting.Planning.ReportPlanGrouping;
using PlanMeasure = NGB.Runtime.Reporting.Planning.ReportPlanMeasure;
using PlanParameter = NGB.Runtime.Reporting.Planning.ReportPlanParameter;
using PlanPredicate = NGB.Runtime.Reporting.Planning.ReportPlanPredicate;
using PlanSort = NGB.Runtime.Reporting.Planning.ReportPlanSort;

namespace NGB.Runtime.Reporting;

public sealed class LedgerAnalysisComposableReportExecutor(
    ReportExecutionPlanner planner,
    ITabularReportPlanExecutor tabularExecutor,
    ILedgerAnalysisFlatDetailReader flatDetailReader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => "accounting.ledger.analysis";

    private readonly ReportExecutionPlanner _planner = planner ?? throw new NgbConfigurationViolationException("Ledger analysis cursor executor requires a planner registration.");
    private readonly ITabularReportPlanExecutor _tabularExecutor = tabularExecutor ?? throw new NgbConfigurationViolationException("Ledger analysis cursor executor requires a tabular executor registration.");
    private readonly ILedgerAnalysisFlatDetailReader _flatDetailReader = flatDetailReader ?? throw new NgbConfigurationViolationException("Ledger analysis cursor executor requires a flat detail reader registration.");

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        if (definition is null)
            throw new NgbArgumentRequiredException(nameof(definition));

        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var runtime = new ReportDefinitionRuntimeModel(definition);
        var effectiveLayout = runtime.GetEffectiveLayout(request);
        var context = new ReportExecutionContext(runtime, request, effectiveLayout);
        var plan = _planner.BuildPlan(context);

        if (!IsCursorEligible(plan, request))
        {
            if (!string.IsNullOrWhiteSpace(request.Cursor))
                throw new NgbArgumentInvalidException("cursor", "Cursor paging is supported only for flat detail ledger analysis mode without grand totals and with the default ascending period order.");

            return await _tabularExecutor.ExecuteAsync(
                definition,
                request with
                {
                    DisablePaging = request.DisablePaging,
                    Offset = 0,
                    Cursor = null
                },
                plan.ReportCode,
                plan.DatasetCode,
                MapGroups(plan.RowGroups),
                MapGroups(plan.ColumnGroups),
                MapFields(plan.DetailFields),
                MapMeasures(plan.Measures),
                MapSorts(plan.Sorts),
                MapPredicates(plan.Predicates),
                MapParameters(plan.Parameters),
                new ReportPlanPaging(0, plan.Paging.Limit, null),
                ct);
        }

        var cursor = request.DisablePaging || string.IsNullOrWhiteSpace(request.Cursor)
            ? null
            : LedgerAnalysisDetailCursorCodec.Decode(request.Cursor.Trim());

        var page = await _flatDetailReader.GetPageAsync(
            new LedgerAnalysisFlatDetailPageRequest(
                DatasetCode: plan.DatasetCode ?? throw new NgbConfigurationViolationException("Ledger analysis flat detail mode requires a dataset code."),
                DetailFields: plan.DetailFields.Select(x => new LedgerAnalysisFlatDetailFieldSelection(x.FieldCode, x.OutputCode, x.Label, x.DataType)).ToList(),
                Measures: plan.Measures.Select(x => new LedgerAnalysisFlatDetailMeasureSelection(x.MeasureCode, x.OutputCode, x.Label, x.DataType)).ToList(),
                Predicates: plan.Predicates.Select(x => new LedgerAnalysisFlatDetailPredicate(x.FieldCode, x.OutputCode, x.Label, x.DataType, x.Filter.Value.Clone())).ToList(),
                FromUtc: ParseRequiredUtcDate(plan.Parameters, "from_utc"),
                ToUtcExclusive: ParseRequiredUtcDate(plan.Parameters, "to_utc").AddDays(1),
                PageSize: plan.Paging.Limit,
                Cursor: cursor,
                DisablePaging: request.DisablePaging),
            ct);

        return new ReportDataPage(
            Columns: BuildVisibleColumns(plan),
            Rows: page.Rows.Select(x => new ReportDataRow(x.Values)).ToList(),
            Offset: plan.Paging.Offset,
            Limit: request.DisablePaging ? page.Rows.Count : plan.Paging.Limit,
            Total: null,
            HasMore: page.HasMore,
            NextCursor: page.NextCursor is null ? null : LedgerAnalysisDetailCursorCodec.Encode(page.NextCursor),
            Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "runtime-ledger-analysis-flat-detail",
                ["paging"] = "cursor",
                ["layoutMode"] = "flat-detail"
            });
    }

    private static bool IsCursorEligible(ReportQueryPlan plan, ReportExecutionRequestDto request)
    {
        if (plan.Mode != ReportExecutionMode.Composable)
            return false;

        if (string.IsNullOrWhiteSpace(plan.DatasetCode))
            return false;

        if (plan.RowGroups.Count > 0 || plan.ColumnGroups.Count > 0 || plan.Shape.IsPivot)
            return false;

        if (plan.DetailFields.Count == 0)
            return false;

        if (plan.Shape.ShowGrandTotals)
            return false;

        if (plan.Measures.Any(x => x.Aggregation != ReportAggregationKind.Sum))
            return false;

        if (request.Offset != 0)
            return false;

        return UsesSupportedSorts(plan.Sorts);
    }

    private static bool UsesSupportedSorts(IReadOnlyList<PlanSort> sorts)
    {
        if (sorts.Count == 0)
            return true;

        if (sorts.Count != 1)
            return false;

        var sort = sorts[0];
        return sort.MeasureCode is null
               && sort is { AppliesToColumnAxis: false, TimeGrain: null, Direction: ReportSortDirection.Asc }
               && string.Equals(sort.FieldCode, "period_utc", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime ParseRequiredUtcDate(IReadOnlyList<PlanParameter> parameters, string parameterCode)
    {
        var raw = parameters.FirstOrDefault(x => string.Equals(x.ParameterCode, parameterCode, StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(raw) || !DateOnly.TryParse(raw, out var date))
            throw new NgbConfigurationViolationException($"Ledger analysis cursor execution requires parameter '{parameterCode}' in yyyy-MM-dd format.");

        return DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
    }

    private static IReadOnlyList<ReportDataColumn> BuildVisibleColumns(ReportQueryPlan plan)
    {
        var columns = new List<ReportDataColumn>(plan.DetailFields.Count + plan.Measures.Count);
        columns.AddRange(plan.DetailFields.Select(x => new ReportDataColumn(x.OutputCode, x.Label, x.DataType, "detail")));
        columns.AddRange(plan.Measures.Select(x => new ReportDataColumn(x.OutputCode, x.Label, x.DataType, "measure")));
        return columns;
    }

    private static IReadOnlyList<ReportPlanGrouping> MapGroups(IReadOnlyList<PlanGrouping> groups)
        => groups
            .Select(x => new ReportPlanGrouping(
                x.FieldCode,
                x.OutputCode,
                x.Label,
                x.DataType,
                x.TimeGrain,
                x.IsColumnAxis,
                x.IncludeDetails,
                x.IncludeEmpty,
                x.IncludeDescendants,
                x.GroupKey))
            .ToList();

    private static IReadOnlyList<ReportPlanFieldSelection> MapFields(IReadOnlyList<PlanFieldSelection> fields)
        => fields
            .Select(x => new ReportPlanFieldSelection(
                x.FieldCode,
                x.OutputCode,
                x.Label,
                x.DataType))
            .ToList();

    private static IReadOnlyList<ReportPlanMeasure> MapMeasures(IReadOnlyList<PlanMeasure> measures)
        => measures
            .Select(x => new ReportPlanMeasure(
                x.MeasureCode,
                x.OutputCode,
                x.Label,
                x.DataType,
                x.Aggregation,
                x.FormatOverride))
            .ToList();

    private static IReadOnlyList<ReportPlanSort> MapSorts(IReadOnlyList<PlanSort> sorts)
        => sorts
            .Select(x => new ReportPlanSort(
                x.FieldCode,
                x.MeasureCode,
                x.Direction,
                x.TimeGrain,
                x.AppliesToColumnAxis,
                x.GroupKey))
            .ToList();

    private static IReadOnlyList<ReportPlanPredicate> MapPredicates(IReadOnlyList<PlanPredicate> predicates)
        => predicates
            .Select(x => new ReportPlanPredicate(
                x.FieldCode,
                x.OutputCode,
                x.Label,
                x.DataType,
                x.Filter with { Value = x.Filter.Value.Clone() }))
            .ToList();

    private static IReadOnlyList<ReportPlanParameter> MapParameters(IReadOnlyList<PlanParameter> parameters)
        => parameters
            .Select(x => new ReportPlanParameter(x.ParameterCode, x.Value))
            .ToList();
}
