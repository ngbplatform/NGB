using System.Runtime.CompilerServices;
using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Runtime.Reporting.Rendering;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Runs;

/// <summary>Complete execution for canonical and dataset-backed reports. The worker owns a repeatable-read session.</summary>
public sealed class PlannedReportStreamingExecutor(
    ReportExecutionPlanner planner,
    IEnumerable<IReportSpecializedPlanExecutor> specialized,
    IStreamingReportDataSource source,
    IDocumentDisplayReader documentDisplays,
    TimeProvider timeProvider)
    : IStreamingReportExecutor
{
    public string ReportCode => "*";
    private sealed record Input(ReportDefinitionDto Definition, ReportExecutionRequestDto Request);
    private ReportSheetDto? _template;

    public string Prepare(ReportDefinitionDto definition, ReportExecutionRequestDto request)
    {
        var runtime = new ReportDefinitionRuntimeModel(definition);
        _ = planner.BuildPlan(new(runtime, request, runtime.GetEffectiveLayout(request)));
        return JsonSerializer.Serialize(new Input(definition, request));
    }

    public ReportSheetDto Template(string preparedJson)
        => _template ?? throw new NgbInvariantViolationException("Report template is available after execution completes.");

    public async IAsyncEnumerable<ReportRowWrite> ReadAsync(string preparedJson, [EnumeratorCancellation] CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<Input>(preparedJson)!;
        var definition = input.Definition;
        var request = input.Request;
        var runtime = new ReportDefinitionRuntimeModel(definition);
        var plan = planner.BuildPlan(new(runtime, request, runtime.GetEffectiveLayout(request)));

        if (definition.Mode == ReportExecutionMode.Canonical)
        {
            var executor = specialized.Single(x 
                => string.Equals(x.ReportCode, definition.ReportCode, StringComparison.OrdinalIgnoreCase));
            request = executor.PrepareExecution(definition, request, timeProvider.GetUtcNow());
            var ordinal = 0;
            string? cursor = null;

            do
            {
                var page = await executor.ExecuteAsync(definition, request with { Offset = 0, Limit = 500, Cursor = cursor, DisablePaging = false }, ct);
                var sheet = page.PrebuiltSheet ?? throw new NgbInvariantViolationException("Canonical executor must supply a report sheet.");
                _template = sheet with { Rows = [] };

                foreach (var row in sheet.Rows)
                {
                    if (!page.HasMore || row.RowKind != ReportRowKind.Total)
                        yield return new(ordinal++, row);
                }

                if (!page.HasMore)
                    break;

                if (string.IsNullOrEmpty(page.NextCursor) || page.NextCursor == cursor || sheet.Rows.Count == 0)
                    throw new NgbInvariantViolationException("Report source did not advance its continuation cursor.");

                cursor = page.NextCursor;
            } while (true);

            yield break;
        }

        var formatter = new ReportCellFormatter();
        var actions = new ReportComposableCellActionResolver(plan, runtime.Dataset);

        if (plan.Shape.IsPivot && plan.ColumnGroups.Count > 0)
        {
            var pivot = new ReportPivotMatrixBuilder(formatter, new ReportPivotHeaderBuilder(formatter, actions), actions);
            await foreach (var row in pivot.BuildStreamingAsync(runtime, plan, ReadPlanAsync, t => _template = t, ct))
            {
                yield return row;
            }
        }
        else
        {
            var columns = ReportSheetBuilder.BuildColumns(plan);
            _template = new(
                columns, 
                [],
                new(
                    Title: definition.Name,
                    Subtitle: definition.Description,
                    HasRowOutline: plan.RowGroups.Count > 0,
                    Diagnostics: new Dictionary<string, string> { ["executor"] = "postgres-streaming", ["sheetBuilder"] = "streaming-groups" }));
            var groups = new ReportGroupTreeBuilder(formatter, new ReportSubtotalBuilder(formatter), actions);

            await foreach (var row in groups.BuildStreamingAsync(plan, columns, ReadPlanAsync, ct))
            {
                yield return row;
            }
        }
    }

    private async IAsyncEnumerable<ReportDataRow> ReadPlanAsync(
        ReportQueryPlan plan,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var query = new ReportDataQuery(
            plan.ReportCode,
            plan.DatasetCode,
            ReportEngine.MapGroups(plan.RowGroups),
            ReportEngine.MapGroups(plan.ColumnGroups),
            ReportEngine.MapFields(plan.DetailFields),
            ReportEngine.MapMeasures(plan.Measures),
            ReportEngine.MapSorts(plan.Sorts),
            ReportEngine.MapPredicates(plan.Predicates),
            ReportEngine.MapParameters(plan.Parameters));

        await foreach (var batch in source.ReadAsync(query, ct))
        {
            var enriched = await ReportEngine.EnrichInteractiveFieldsAsync(plan, batch, documentDisplays, ct);
            foreach (var row in enriched.Rows)
            {
                yield return row;
            }
        }
    }
}
