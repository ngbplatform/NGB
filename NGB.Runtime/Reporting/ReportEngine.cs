using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.Runtime.Reporting;

public sealed class ReportEngine(
    IReportDefinitionProvider definitions,
    IReportLayoutValidator validator,
    ReportExecutionPlanner planner,
    IReportPlanExecutor executor,
    ReportSheetBuilder sheetBuilder ,
    ReportVariantRequestResolver? variantResolver = null,
    ReportFilterScopeExpander? filterScopeExpander = null,
    IDocumentDisplayReader? documentDisplayReader = null,
    ReportPagedQueryExecutor? pagedQueries = null,
    AccountingSummaryPagedExecutor? accountingPages = null,
    AccountingConsistencyPagedExecutor? consistencyPages = null)
    : IReportEngine
{
    private const int DefaultRenderedSourceRowLimit = 10_000;

    private readonly IReportDefinitionProvider _definitions = definitions
        ?? throw new NgbConfigurationViolationException("Reporting engine requires a definition provider registration.");

    private readonly IReportLayoutValidator _validator = validator
        ?? throw new NgbConfigurationViolationException("Reporting engine requires a layout validator registration.");

    private readonly ReportExecutionPlanner _planner = planner
        ?? throw new NgbConfigurationViolationException("Reporting engine requires a planner registration.");

    private readonly IReportPlanExecutor _executor = executor
        ?? throw new NgbConfigurationViolationException("Reporting engine requires a plan executor registration.");

    private readonly ReportSheetBuilder _sheetBuilder = sheetBuilder
        ?? throw new NgbConfigurationViolationException("Reporting engine requires a sheet builder registration.");

    public async Task<ReportExecutionResponseDto> ExecuteAsync(
        string reportCode,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var result = await ExecuteCoreAsync(
            reportCode,
            request,
            ct,
            hardSourceRowLimit: request.DisablePaging ? DefaultRenderedSourceRowLimit : null);

        return BuildResponse(result.ReportCode, result.Engine, result.Execution);
    }

    public async Task<ReportSheetDto> ExecuteExportSheetAsync(
        string reportCode,
        ReportExportRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var execution = new ReportExecutionRequestDto(
            Layout: request.Layout,
            Filters: request.Filters,
            Parameters: request.Parameters,
            VariantCode: request.VariantCode,
            DisablePaging: false);

        var result = await ExecuteCoreAsync(
            reportCode,
            execution,
            ct,
            hardSourceRowLimit: DefaultRenderedSourceRowLimit);

        return result.Execution.Sheet;
    }

    private async Task<ReportEngineExecutionEnvelope> ExecuteCoreAsync(
        string reportCode,
        ReportExecutionRequestDto request,
        CancellationToken ct,
        int? hardSourceRowLimit = null)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var definition = await _definitions.GetDefinitionAsync(reportCode, ct);
        var requestWithVariant = variantResolver is null
            ? request
            : await variantResolver.ResolveAsync(reportCode, request, ct);

        requestWithVariant = NormalizeInteractivePaging(requestWithVariant);
        _validator.Validate(definition, requestWithVariant);
        requestWithVariant = NormalizeRequestMaps(requestWithVariant);

        var runtime = new ReportDefinitionRuntimeModel(definition);
        var effectiveRequest = filterScopeExpander is null
            ? requestWithVariant
            : await filterScopeExpander.ExpandAsync(runtime, requestWithVariant, ct);

        if (hardSourceRowLimit is null
            && !effectiveRequest.DisablePaging
            && accountingPages is not null
            && AccountingSummaryPagedExecutor.Supports(runtime.ReportCodeNorm))
        {
            return new(
                runtime.ReportCodeNorm,
                "runtime",
                await accountingPages.ExecuteAsync(definition, effectiveRequest, ct));
        }

        if (hardSourceRowLimit is null
            && !effectiveRequest.DisablePaging
            && consistencyPages is not null
            && runtime.ReportCodeNorm == Core.Reporting.AccountingReportCodes.Consistency)
        {
            return new(
                runtime.ReportCodeNorm,
                "runtime",
                await consistencyPages.ExecuteAsync(definition, effectiveRequest, ct));
        }

        var effectiveLayout = runtime.GetEffectiveLayout(effectiveRequest);
        var context = new ReportExecutionContext(runtime, effectiveRequest, effectiveLayout);
        var plan = _planner.BuildPlan(context);

        if (hardSourceRowLimit is null
            && pagedQueries is not null
            && definition.Mode == ReportExecutionMode.Composable
            && !effectiveRequest.DisablePaging)
        {
            var pagedResult = await pagedQueries.ExecuteAsync(runtime, plan, effectiveRequest, ct);
            return new(runtime.ReportCodeNorm, "runtime", pagedResult);
        }

        var resolvedHardSourceRowLimit = hardSourceRowLimit is null
            ? (int?)null
            : Math.Min(hardSourceRowLimit.Value, ResolveRenderedSourceRowLimit(runtime));

        var executorRequest = resolvedHardSourceRowLimit is { } hardLimit
            ? effectiveRequest with { DisablePaging = false, Offset = 0, Limit = checked(hardLimit + 1), Cursor = null }
            : effectiveRequest;

        var executorPaging = resolvedHardSourceRowLimit is { } sourceLimit
            ? new ReportPlanPaging(0, checked(sourceLimit + 1))
            : new ReportPlanPaging(plan.Paging.Offset, plan.Paging.Limit, plan.Paging.Cursor);

        var page = await _executor.ExecuteAsync(
            definition,
            executorRequest,
            plan.ReportCode,
            plan.DatasetCode,
            MapGroups(plan.RowGroups),
            MapGroups(plan.ColumnGroups),
            MapFields(plan.DetailFields),
            MapMeasures(plan.Measures),
            MapSorts(plan.Sorts),
            MapPredicates(plan.Predicates),
            MapParameters(plan.Parameters),
            executorPaging,
            ct);

        if (resolvedHardSourceRowLimit is { } hardSourceLimit)
        {
            EnsureHardSourceRowCap(runtime, plan, page, hardSourceLimit);
            // DisablePaging is executed internally as a bounded page (limit + 1)
            // so exports and interactive requests cannot materialize unbounded
            // data. Once the cap check proves the complete result fits, its row
            // count is also the exact total expected by the public contract.
            if (effectiveRequest.DisablePaging && page.Total is null)
                page = page with { Total = page.Rows.Count };
        }

        page = await EnrichInteractiveFieldsAsync(plan, page, documentDisplayReader, ct);
        var fullSheet = _sheetBuilder.BuildSheet(runtime, plan, page);

        var result = new ReportExecutionResult(
                Sheet: fullSheet,
                Offset: page.Offset,
                Limit: page.Limit,
                Total: page.Total,
                HasMore: page.HasMore,
                NextCursor: page.NextCursor,
                Diagnostics: page.Diagnostics);

        return new ReportEngineExecutionEnvelope(runtime.ReportCodeNorm, "runtime", result);
    }

    private static ReportExecutionRequestDto NormalizeInteractivePaging(ReportExecutionRequestDto request)
        => request with
        {
            Offset = Math.Clamp(request.Offset, 0, PagingLimits.MaxOffset),
            Limit = request.Limit > PagingLimits.MaxPageSize
                ? PagingLimits.MaxPageSize
                : request.Limit
        };

    internal static ReportExecutionRequestDto NormalizeRequestMaps(ReportExecutionRequestDto request)
        => request with
        {
            Filters = request.Filters?.ToDictionary(
                pair => CodeNormalizer.NormalizeCodeNorm(pair.Key, nameof(pair.Key)),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase),
            Parameters = request.Parameters?.ToDictionary(
                pair => CodeNormalizer.NormalizeCodeNorm(pair.Key, nameof(pair.Key)),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase)
        };

    private static int ResolveRenderedSourceRowLimit(ReportDefinitionRuntimeModel runtime)
        => Math.Clamp(
            runtime.Capabilities.MaxVisibleRows ?? DefaultRenderedSourceRowLimit,
            1,
            DefaultRenderedSourceRowLimit);

    private static void EnsureHardSourceRowCap(
        ReportDefinitionRuntimeModel runtime,
        ReportQueryPlan plan,
        ReportDataPage page,
        int sourceRowLimit)
    {
        if (!page.HasMore && page.Rows.Count <= sourceRowLimit && page.Total.GetValueOrDefault() <= sourceRowLimit)
            return;

        var fieldPath = plan.RowGroups.Count > 0 || plan.DetailFields.Count > 0
            ? "layout.rowGroups"
            : plan.ColumnGroups.Count > 0
                ? "layout.columnGroups"
                : "layout.measures";
        throw new NGB.Core.Reporting.Exceptions.ReportLayoutValidationException(
            $"The report requires more than {sourceRowLimit} source rows. Narrow the filters and try again.",
            fieldPath,
            errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [fieldPath] = [$"Report source rows must not exceed {sourceRowLimit}."]
            });
    }

    internal static async Task<ReportDataPage> EnrichInteractiveFieldsAsync(
        ReportQueryPlan plan,
        ReportDataPage page,
        IDocumentDisplayReader? documentDisplayReader,
        CancellationToken ct)
    {
        if (documentDisplayReader is null)
            return page;

        var documentOutputCodes = plan.RowGroups
            .Where(x => x.FieldCode.Equals("document_display", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.OutputCode)
            .Concat(
                plan.ColumnGroups
                    .Where(x => x.FieldCode.Equals("document_display", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.OutputCode))
            .Concat(
                plan.DetailFields
                    .Where(x => x.FieldCode.Equals("document_display", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.OutputCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (documentOutputCodes.Length == 0)
            return page;

        var ids = page.Rows
            .SelectMany(x => new[]
            {
                x.Values.GetValueOrDefault(ReportInteractiveSupport.SupportDocumentId),
                x.Values.GetValueOrDefault("document_id")
            })
            .Select(TryConvertGuid)
            .Where(x => x.HasValue && x.Value != Guid.Empty)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
            return page;

        var refs = await documentDisplayReader.ResolveRefsAsync(ids, ct);
        var updatedRows = new List<ReportDataRow>(page.Rows.Count);
        var changed = false;

        foreach (var row in page.Rows)
        {
            var documentId = TryConvertGuid(row.Values.GetValueOrDefault(ReportInteractiveSupport.SupportDocumentId))
                ?? TryConvertGuid(row.Values.GetValueOrDefault("document_id"));

            if (!documentId.HasValue || !refs.TryGetValue(documentId.Value, out var documentRef))
            {
                updatedRows.Add(row);
                continue;
            }

            var values = new Dictionary<string, object?>(row.Values, StringComparer.OrdinalIgnoreCase);
            foreach (var outputCode in documentOutputCodes)
            {
                values[outputCode] = documentRef.Display;
            }

            values[ReportInteractiveSupport.SupportDocumentType] = documentRef.TypeCode;
            updatedRows.Add(new ReportDataRow(values));
            changed = true;
        }

        return changed ? page with { Rows = updatedRows } : page;
    }

    private static Guid? TryConvertGuid(object? raw)
        => raw switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(text, out var parsed) => parsed,
            _ => null
        };

    private static ReportExecutionResponseDto BuildResponse(
        string reportCode,
        string engine,
        ReportExecutionResult result)
    {
        var diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["reportCode"] = reportCode,
            ["engine"] = engine,
            ["executor"] = result.Diagnostics?.TryGetValue("executor", out var executor) == true
                ? executor
                : "unknown"
        };

        if (result.Diagnostics is not null)
        {
            foreach (var pair in result.Diagnostics)
            {
                diagnostics.TryAdd(pair.Key, pair.Value);
            }
        }

        return new ReportExecutionResponseDto(
            Sheet: result.Sheet,
            Offset: result.Offset,
            Limit: result.Limit,
            Total: result.Total,
            HasMore: result.HasMore,
            NextCursor: result.NextCursor,
            Diagnostics: diagnostics);
    }

    internal static IReadOnlyList<ReportPlanGrouping> MapGroups(IReadOnlyList<Planning.ReportPlanGrouping> groups)
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

    internal static IReadOnlyList<ReportPlanFieldSelection> MapFields(IReadOnlyList<Planning.ReportPlanFieldSelection> fields)
        => fields
            .Select(x => new ReportPlanFieldSelection(
                x.FieldCode,
                x.OutputCode,
                x.Label,
                x.DataType))
            .ToList();

    internal static IReadOnlyList<ReportPlanMeasure> MapMeasures(IReadOnlyList<Planning.ReportPlanMeasure> measures)
        => measures
            .Select(x => new ReportPlanMeasure(
                x.MeasureCode,
                x.OutputCode,
                x.Label,
                x.DataType,
                x.Aggregation,
                x.FormatOverride))
            .ToList();

    internal static IReadOnlyList<ReportPlanSort> MapSorts(IReadOnlyList<Planning.ReportPlanSort> sorts)
        => sorts
            .Select(x => new ReportPlanSort(
                x.FieldCode,
                x.MeasureCode,
                x.Direction,
                x.TimeGrain,
                x.AppliesToColumnAxis,
                x.GroupKey))
            .ToList();

    internal static IReadOnlyList<ReportPlanPredicate> MapPredicates(IReadOnlyList<Planning.ReportPlanPredicate> predicates)
        => predicates
            .Select(x => new ReportPlanPredicate(
                x.FieldCode,
                x.OutputCode,
                x.Label,
                x.DataType,
                x.Filter,
                x.TimeGrain))
            .ToList();

    internal static IReadOnlyList<ReportPlanParameter> MapParameters(IReadOnlyList<Planning.ReportPlanParameter> parameters)
        => parameters.Select(x => new ReportPlanParameter(x.ParameterCode, x.Value)).ToList();

    private sealed record ReportEngineExecutionEnvelope(
        string ReportCode,
        string Engine,
        ReportExecutionResult Execution);
}
