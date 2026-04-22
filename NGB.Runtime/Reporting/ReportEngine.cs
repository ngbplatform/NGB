using System.Text;
using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Runtime.Reporting.Internal;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;

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
    IRenderedReportSnapshotStore? renderedReportSnapshotStore = null)
    : IReportEngine
{
    private readonly IReportDefinitionProvider _definitions = definitions ?? throw new NgbConfigurationViolationException("Reporting engine requires a definition provider registration.");
    private readonly IReportLayoutValidator _validator = validator ?? throw new NgbConfigurationViolationException("Reporting engine requires a layout validator registration.");
    private readonly ReportExecutionPlanner _planner = planner ?? throw new NgbConfigurationViolationException("Reporting engine requires a planner registration.");
    private readonly IReportPlanExecutor _executor = executor ?? throw new NgbConfigurationViolationException("Reporting engine requires a plan executor registration.");
    private readonly ReportSheetBuilder _sheetBuilder = sheetBuilder ?? throw new NgbConfigurationViolationException("Reporting engine requires a sheet builder registration.");
    private readonly IRenderedReportSnapshotStore _renderedReportSnapshotStore = renderedReportSnapshotStore ?? NullRenderedReportSnapshotStore.Instance;

    public async Task<ReportExecutionResponseDto> ExecuteAsync(
        string reportCode,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var result = await ExecuteCoreAsync(reportCode, request, ct);
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
            DisablePaging: true);

        var result = await ExecuteCoreAsync(reportCode, execution, ct);
        return result.Execution.Sheet;
    }

    private async Task<ReportEngineExecutionEnvelope> ExecuteCoreAsync(
        string reportCode,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var definition = await _definitions.GetDefinitionAsync(reportCode, ct);
        var requestWithVariant = variantResolver is null
            ? request
            : await variantResolver.ResolveAsync(reportCode, request, ct);
        _validator.Validate(definition, requestWithVariant);

        var runtime = new ReportDefinitionRuntimeModel(definition);
        var effectiveRequest = filterScopeExpander is null
            ? requestWithVariant
            : await filterScopeExpander.ExpandAsync(runtime, requestWithVariant, ct);

        var effectiveLayout = runtime.GetEffectiveLayout(effectiveRequest);
        var context = new ReportExecutionContext(runtime, effectiveRequest, effectiveLayout);
        var plan = _planner.BuildPlan(context);
        var useRenderedSheetPaging = ShouldUseRenderedSheetPaging(runtime, effectiveRequest, plan);
        var pageSize = ResolveRequestedPageSize(runtime, effectiveRequest);

        if (useRenderedSheetPaging)
        {
            var fingerprint = ComputeRenderedSnapshotFingerprint(plan);
            var cursor = DecodeRenderedSheetCursor(effectiveRequest);

            if (cursor is { SnapshotId: { } snapshotId, Fingerprint: { } cursorFingerprint }
                && cursorFingerprint != fingerprint)
            {
                throw new NgbArgumentInvalidException("cursor", "Cursor does not match the current report definition, layout, filters, or parameters.");
            }

            if (cursor?.SnapshotId is { } existingSnapshotId)
            {
                var cached = await _renderedReportSnapshotStore.GetAsync(existingSnapshotId, ct);
                if (cached is not null
                    && string.Equals(cached.ReportCode, runtime.ReportCodeNorm, StringComparison.OrdinalIgnoreCase)
                    && cached.Fingerprint == fingerprint)
                {
                    var cachedResult = BuildRenderedSheetPagedResult(
                        cached,
                        offset: cursor.Offset,
                        limit: pageSize,
                        cursorMode: "snapshot-hit");

                    if (!cachedResult.HasMore)
                        await _renderedReportSnapshotStore.RemoveAsync(existingSnapshotId, ct);

                    return new ReportEngineExecutionEnvelope(runtime.ReportCodeNorm, "runtime", cachedResult);
                }
            }
        }

        var executorRequest = useRenderedSheetPaging
            ? effectiveRequest with
            {
                DisablePaging = true,
                Offset = 0,
                Cursor = null
            }
            : effectiveRequest;
        var executorPaging = useRenderedSheetPaging
            ? new ReportPlanPaging(0, pageSize)
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
        page = await EnrichInteractiveFieldsAsync(plan, page, ct);
        var fullSheet = _sheetBuilder.BuildSheet(runtime, plan, page);

        var result = useRenderedSheetPaging
            ? await BuildRenderedSheetPagedResultAsync(runtime, effectiveRequest, plan, page, fullSheet, pageSize, ct)
            : new ReportExecutionResult(
                Sheet: fullSheet,
                Offset: page.Offset,
                Limit: page.Limit,
                Total: page.Total,
                HasMore: page.HasMore,
                NextCursor: page.NextCursor,
                Diagnostics: page.Diagnostics);

        return new ReportEngineExecutionEnvelope(runtime.ReportCodeNorm, "runtime", result);
    }

    private static bool ShouldUseRenderedSheetPaging(
        ReportDefinitionRuntimeModel runtime,
        ReportExecutionRequestDto request,
        ReportQueryPlan plan)
        => runtime.Definition.Mode == ReportExecutionMode.Composable
           && !request.DisablePaging
           && runtime.Definition.Presentation?.GroupedPagingMode != ReportGroupedPagingMode.BoundedNoCursor
           && (plan.RowGroups.Count > 0
               || plan.ColumnGroups.Count > 0
               || plan.Shape.ShowGrandTotals
               || plan.Shape.ShowSubtotals);

    private static int ResolveRequestedPageSize(
        ReportDefinitionRuntimeModel runtime,
        ReportExecutionRequestDto request)
    {
        if (request.Limit > 0)
            return request.Limit;

        return runtime.Definition.Presentation?.InitialPageSize is > 0
            ? runtime.Definition.Presentation.InitialPageSize.Value
            : 100;
    }

    private async Task<ReportExecutionResult> BuildRenderedSheetPagedResultAsync(
        ReportDefinitionRuntimeModel runtime,
        ReportExecutionRequestDto request,
        ReportQueryPlan plan,
        ReportDataPage page,
        ReportSheetDto fullSheet,
        int limit,
        CancellationToken ct)
    {
        var fingerprint = ComputeRenderedSnapshotFingerprint(plan);
        var offset = DecodeRenderedSheetCursor(request)?.Offset ?? Math.Max(0, request.Offset);
        var snapshot = CreateRenderedSnapshot(runtime.ReportCodeNorm, fingerprint, fullSheet, page.Diagnostics);
        var result = BuildRenderedSheetPagedResult(snapshot, offset, limit, cursorMode: "snapshot-materialized");

        if (result.HasMore)
        {
            var stored = await _renderedReportSnapshotStore.SetAsync(snapshot, ct);
            if (!stored)
            {
                return result with
                {
                    NextCursor = RenderedSheetCursorCodec.EncodeOffsetOnly(offset + CountContentRows(result.Sheet.Rows)),
                    Diagnostics = MergeDiagnostics(result.Diagnostics, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["snapshotCache"] = "unavailable"
                    })
                };
            }

            return result with
            {
                NextCursor = RenderedSheetCursorCodec.EncodeSnapshot(snapshot.SnapshotId, offset + CountContentRows(result.Sheet.Rows), snapshot.Fingerprint),
                Diagnostics = MergeDiagnostics(result.Diagnostics, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["snapshotCache"] = "created",
                    ["snapshotId"] = snapshot.SnapshotId.ToString("D")
                })
            };
        }

        await _renderedReportSnapshotStore.RemoveAsync(snapshot.SnapshotId, ct);
        return result;
    }

    private static ReportExecutionResult BuildRenderedSheetPagedResult(
        RenderedReportSnapshot snapshot,
        int offset,
        int limit,
        string cursorMode)
    {
        var clampedOffset = Math.Max(0, offset);
        var pageRows = snapshot.ContentRows.Skip(clampedOffset).Take(limit).ToList();
        var nextOffset = clampedOffset + pageRows.Count;
        var hasMore = nextOffset < snapshot.TotalContentRows;

        if (!hasMore && snapshot.GrandTotalRow is not null)
            pageRows.Add(snapshot.GrandTotalRow);

        var sheet = CloneSheetWithRows(snapshot.TemplateSheet, pageRows, snapshot.TotalContentRows, clampedOffset, limit, hasMore, cursorMode);

        return new ReportExecutionResult(
            Sheet: sheet,
            Offset: clampedOffset,
            Limit: limit,
            Total: snapshot.TotalContentRows,
            HasMore: hasMore,
            NextCursor: null,
            Diagnostics: MergeDiagnostics(snapshot.Diagnostics, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["paging"] = "rendered-sheet-v2",
                ["cursorMode"] = cursorMode,
                ["renderedRowOffset"] = clampedOffset.ToString(),
                ["renderedRowLimit"] = limit.ToString(),
                ["renderedRowTotal"] = snapshot.TotalContentRows.ToString()
            }));
    }

    private static RenderedSheetCursor? DecodeRenderedSheetCursor(ReportExecutionRequestDto request)
        => string.IsNullOrWhiteSpace(request.Cursor)
            ? null
            : RenderedSheetCursorCodec.Decode(request.Cursor.Trim());

    private static bool IsGrandTotalRow(ReportSheetRowDto row)
        => row.RowKind == ReportRowKind.Total
           || string.Equals(row.SemanticRole, "grand-total", StringComparison.OrdinalIgnoreCase)
           || string.Equals(row.SemanticRole, "grand_total", StringComparison.OrdinalIgnoreCase);

    private static RenderedReportSnapshot CreateRenderedSnapshot(
        string reportCode,
        Guid fingerprint,
        ReportSheetDto sheet,
        IReadOnlyDictionary<string, string>? diagnostics)
    {
        var contentRows = new List<ReportSheetRowDto>(sheet.Rows.Count);
        ReportSheetRowDto? grandTotalRow = null;

        foreach (var row in sheet.Rows)
        {
            if (IsGrandTotalRow(row))
            {
                grandTotalRow = row;
                continue;
            }

            contentRows.Add(row);
        }

        return new RenderedReportSnapshot(
            SnapshotId: Guid.CreateVersion7(),
            ReportCode: reportCode,
            Fingerprint: fingerprint,
            TemplateSheet: sheet with { Rows = [] },
            ContentRows: contentRows,
            GrandTotalRow: grandTotalRow,
            TotalContentRows: contentRows.Count,
            Diagnostics: MergeDiagnostics(sheet.Meta?.Diagnostics, diagnostics));
    }

    private static Guid ComputeRenderedSnapshotFingerprint(ReportQueryPlan plan)
    {
        var sb = new StringBuilder();
        sb.Append(plan.ReportCode).Append('|');
        sb.Append(plan.Mode).Append('|');
        AppendGroups(sb, plan.RowGroups);
        sb.Append('|');
        AppendGroups(sb, plan.ColumnGroups);
        sb.Append('|');
        AppendFields(sb, plan.DetailFields);
        sb.Append('|');
        AppendMeasures(sb, plan.Measures);
        sb.Append('|');
        AppendSorts(sb, plan.Sorts);
        sb.Append('|');
        AppendPredicates(sb, plan.Predicates);
        sb.Append('|');
        AppendParameters(sb, plan.Parameters);
        sb.Append('|')
            .Append(plan.Shape.ShowDetails ? '1' : '0')
            .Append(plan.Shape.ShowSubtotals ? '1' : '0')
            .Append(plan.Shape.ShowSubtotalsOnSeparateRows ? '1' : '0')
            .Append(plan.Shape.ShowGrandTotals ? '1' : '0')
            .Append(plan.Shape.IsPivot ? '1' : '0');

        return DeterministicGuid.Create($"RenderedReportSnapshot|{sb}");
    }

    private static void AppendGroups(StringBuilder sb, IReadOnlyList<NGB.Runtime.Reporting.Planning.ReportPlanGrouping> groups)
    {
        foreach (var group in groups)
        {
            sb.Append(group.FieldCode).Append(':')
                .Append(group.OutputCode).Append(':')
                .Append(group.Label).Append(':')
                .Append(group.DataType).Append(':')
                .Append(group.TimeGrain?.ToString() ?? string.Empty).Append(':')
                .Append(group.IsColumnAxis ? '1' : '0').Append(':')
                .Append(group.IncludeDetails ? '1' : '0').Append(':')
                .Append(group.IncludeEmpty ? '1' : '0').Append(':')
                .Append(group.IncludeDescendants ? '1' : '0').Append(':')
                .Append(group.GroupKey ?? string.Empty).Append(';');
        }
    }

    private static void AppendFields(StringBuilder sb, IReadOnlyList<NGB.Runtime.Reporting.Planning.ReportPlanFieldSelection> fields)
    {
        foreach (var field in fields)
        {
            sb.Append(field.FieldCode).Append(':')
                .Append(field.OutputCode).Append(':')
                .Append(field.Label).Append(':')
                .Append(field.DataType).Append(';');
        }
    }

    private static void AppendMeasures(StringBuilder sb, IReadOnlyList<NGB.Runtime.Reporting.Planning.ReportPlanMeasure> measures)
    {
        foreach (var measure in measures)
        {
            sb.Append(measure.MeasureCode).Append(':')
                .Append(measure.OutputCode).Append(':')
                .Append(measure.Label).Append(':')
                .Append(measure.DataType).Append(':')
                .Append(measure.Aggregation).Append(':')
                .Append(measure.FormatOverride ?? string.Empty).Append(';');
        }
    }

    private static void AppendSorts(StringBuilder sb, IReadOnlyList<NGB.Runtime.Reporting.Planning.ReportPlanSort> sorts)
    {
        foreach (var sort in sorts)
        {
            sb.Append(sort.FieldCode).Append(':')
                .Append(sort.MeasureCode ?? string.Empty).Append(':')
                .Append(sort.Direction).Append(':')
                .Append(sort.TimeGrain?.ToString() ?? string.Empty).Append(':')
                .Append(sort.AppliesToColumnAxis ? '1' : '0').Append(':')
                .Append(sort.GroupKey ?? string.Empty).Append(';');
        }
    }

    private static void AppendPredicates(StringBuilder sb, IReadOnlyList<NGB.Runtime.Reporting.Planning.ReportPlanPredicate> predicates)
    {
        foreach (var predicate in predicates.OrderBy(x => x.FieldCode, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(predicate.FieldCode).Append(':')
                .Append(predicate.OutputCode).Append(':')
                .Append(predicate.Label).Append(':')
                .Append(predicate.DataType).Append(':')
                .Append(JsonSerializer.Serialize(predicate.Filter.Value)).Append(';');
        }
    }

    private static void AppendParameters(StringBuilder sb, IReadOnlyList<NGB.Runtime.Reporting.Planning.ReportPlanParameter> parameters)
    {
        foreach (var parameter in parameters.OrderBy(x => x.ParameterCode, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(parameter.ParameterCode).Append(':')
                .Append(parameter.Value).Append(';');
        }
    }

    private static int CountContentRows(IReadOnlyList<ReportSheetRowDto> rows)
        => rows.Count(static row => !IsGrandTotalRow(row));

    private static ReportSheetDto CloneSheetWithRows(
        ReportSheetDto sheet,
        IReadOnlyList<ReportSheetRowDto> rows,
        int total,
        int offset,
        int limit,
        bool hasMore,
        string cursorMode)
    {
        var diagnostics = new Dictionary<string, string>(sheet.Meta?.Diagnostics ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
        {
            ["paging"] = "rendered-sheet-v2",
            ["cursorMode"] = cursorMode,
            ["renderedRowOffset"] = offset.ToString(),
            ["renderedRowLimit"] = limit.ToString(),
            ["renderedRowCount"] = rows.Count.ToString(),
            ["renderedRowTotal"] = total.ToString(),
            ["hasMore"] = hasMore.ToString()
        };

        return sheet with
        {
            Rows = rows,
            Meta = sheet.Meta is null
                ? null
                : sheet.Meta with
                {
                    Diagnostics = diagnostics
                }
        };
    }

    private static IReadOnlyDictionary<string, string>? MergeDiagnostics(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (left is null && right is null)
            return null;

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (left is not null)
        {
            foreach (var pair in left)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        if (right is not null)
        {
            foreach (var pair in right)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        return merged;
    }

    private async Task<ReportDataPage> EnrichInteractiveFieldsAsync(
        ReportQueryPlan plan,
        ReportDataPage page,
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

    private static IReadOnlyList<ReportPlanGrouping> MapGroups(IReadOnlyList<Planning.ReportPlanGrouping> groups)
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

    private static IReadOnlyList<ReportPlanFieldSelection> MapFields(IReadOnlyList<Planning.ReportPlanFieldSelection> fields)
        => fields
            .Select(x => new ReportPlanFieldSelection(
                x.FieldCode,
                x.OutputCode,
                x.Label,
                x.DataType))
            .ToList();

    private static IReadOnlyList<ReportPlanMeasure> MapMeasures(IReadOnlyList<Planning.ReportPlanMeasure> measures)
        => measures
            .Select(x => new ReportPlanMeasure(
                x.MeasureCode,
                x.OutputCode,
                x.Label,
                x.DataType,
                x.Aggregation,
                x.FormatOverride))
            .ToList();

    private static IReadOnlyList<ReportPlanSort> MapSorts(IReadOnlyList<Planning.ReportPlanSort> sorts)
        => sorts
            .Select(x => new ReportPlanSort(
                x.FieldCode,
                x.MeasureCode,
                x.Direction,
                x.TimeGrain,
                x.AppliesToColumnAxis,
                x.GroupKey))
            .ToList();

    private static IReadOnlyList<ReportPlanPredicate> MapPredicates(IReadOnlyList<Planning.ReportPlanPredicate> predicates)
        => predicates
            .Select(x => new ReportPlanPredicate(
                x.FieldCode,
                x.OutputCode,
                x.Label,
                x.DataType,
                x.Filter))
            .ToList();

    private static IReadOnlyList<ReportPlanParameter> MapParameters(IReadOnlyList<Planning.ReportPlanParameter> parameters)
        => parameters.Select(x => new ReportPlanParameter(x.ParameterCode, x.Value)).ToList();

    private sealed record ReportEngineExecutionEnvelope(
        string ReportCode,
        string Engine,
        ReportExecutionResult Execution);
}
