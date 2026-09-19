using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Runtime.Reporting.Rendering;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

/// <summary>Pages aggregation keys first, then fetches their cells in one bounded set-based query.</summary>
public sealed class ReportPagedQueryExecutor(IReportPageDataSource source, IDocumentDisplayReader documentDisplays)
{
    private const int MaxColumns = 512;
    private const int MaxPageCells = 50_000;

    public async Task<ReportExecutionResult> ExecuteAsync(
        ReportDefinitionRuntimeModel definition,
        ReportQueryPlan original,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var path = request.GroupPath ?? [];
        if (path.Count > original.RowGroups.Count || path.Any(v => v.ValueKind is JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Undefined))
            throw new NgbArgumentInvalidException("groupPath", "Invalid report group path.");

        var predicates = original.Predicates.ToList();
        for (var i = 0; i < path.Count; i++)
        {
            var g = original.RowGroups[i];
            predicates.Add(new(g.FieldCode, g.OutputCode, g.Label, g.DataType, new(path[i]), g.TimeGrain));
        }
        
        var group = original.RowGroups.Skip(path.Count).Take(1).ToArray();
        var details = group.Length == 0 ? original.DetailFields : [];
        var selected = group
            .Select(g => g.FieldCode)
            .Concat(details.Select(d => d.FieldCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var plan = original with
        {
            RowGroups = group,
            DetailFields = details,
            Predicates = predicates,
            Sorts = original.Sorts.Where(s => s.AppliesToColumnAxis
                        || s.MeasureCode is not null
                        || selected.Contains(s.FieldCode)
                    && (s.GroupKey is null || group.Any(g => g.GroupKey == s.GroupKey)))
                .ToArray()
        };

        var rowPlan = plan with
        {
            ColumnGroups = [],
            Sorts = plan.Sorts.Where(s => !s.AppliesToColumnAxis).ToArray()
        };

        var pivot = plan.ColumnGroups.Count > 0;
        var columnRows = Array.Empty<ReportDataRow>();
        var width = ReportSheetBuilder.BuildColumns(rowPlan).Count;
        
        if (pivot)
        {
            var columnPlan = plan with
            {
                RowGroups = [], DetailFields = [],
                Sorts = plan.Sorts.Where(s => s.AppliesToColumnAxis).ToArray()
            };

            var maxWidth = Math.Min(MaxColumns, definition.Capabilities.MaxVisibleColumns ?? MaxColumns);
            var available = maxWidth - (group.Length > 0 ? 1 : details.Count) - (plan.Shape.ShowGrandTotals ? plan.Measures.Count : 0);
            var maxLeaves = available / Math.Max(1, plan.Measures.Count);

            if (maxLeaves < 1)
                throw TooWide(maxWidth);

            var columns = await ReadAsync(columnPlan, new(0, maxLeaves), null, ct);

            if (columns.HasMore)
                throw TooWide(maxWidth);

            columnRows = columns.Rows.ToArray();
            width = (group.Length > 0 ? 1 : details.Count) + columnRows.Length * plan.Measures.Count + (plan.Shape.ShowGrandTotals ? plan.Measures.Count : 0);
        }

        var separateSubtotals = group.Length > 0
            && plan.Shape is { ShowSubtotals: true, ShowSubtotalsOnSeparateRows: true }
            && path.Count < original.RowGroups.Count - 1;

        var showGroupValues = plan.Shape.ShowSubtotals
            ? !separateSubtotals
            : original.DetailFields.Count == 0 && path.Count == original.RowGroups.Count - 1;

        var cellBudget = Math.Min(MaxPageCells, definition.Capabilities.MaxRenderedCells ?? MaxPageCells);
        var pageSize = Math.Clamp(request.Limit, 1, Math.Min(500, Math.Max(1, (cellBudget / Math.Max(1, width) - 1) / (separateSubtotals ? 2 : 1))));
        var rows = await ReadAsync(rowPlan, new(0, pageSize, request.Cursor), null, ct);
        var rawRows = rows;
        rows = await ReportEngine.EnrichInteractiveFieldsAsync(rowPlan, rows, documentDisplays, ct);

        IReadOnlyList<ReportDataRow> cells = [];
        if (pivot && rows.Rows.Count > 0 && columnRows.Length > 0)
        {
            var fields = group
                .Select(g => new ReportSelectionField(g.FieldCode, g.TimeGrain))
                .Concat(details.Select(d => new ReportSelectionField(d.FieldCode)))
                .ToArray();

            var codes = group
                .Select(g => g.OutputCode)
                .Concat(details.Select(d => d.OutputCode))
                .ToArray();

            var selection = fields.Length == 0
                ? null 
                : new ReportRowSelection(
                    fields,
                    rawRows.Rows
                        .Select(r => (IReadOnlyList<JsonElement>)codes
                            .Select(c => JsonSerializer.SerializeToElement(r.Values.GetValueOrDefault(c)))
                            .ToArray())
                        .ToArray());

            var cellPage = await ReadAsync(
                plan,
                new(0, checked(rows.Rows.Count * columnRows.Length)),
                selection,
                ct);

            if (cellPage.HasMore)
                throw new NgbInvariantViolationException("Pivot cells exceeded the selected row and column keys.");

            cells = cellPage.Rows;
        }

        // A complete set of disjoint groups can supply additive totals. Partial pages and non-additive
        // measures still query the original observations (never sum averages or distinct counts).
        ReportDataRow? grand = null;
        IReadOnlyList<ReportDataRow> grandCells = [];
        if (!rows.HasMore && plan.Shape.ShowGrandTotals && plan.Measures.Count > 0)
        {
            var grandPlan = plan with
            {
                RowGroups = [],
                ColumnGroups = [],
                DetailFields = [],
                Sorts = []
            };

            var completeAdditiveGroups = !pivot && group.Length > 0
                && string.IsNullOrEmpty(request.Cursor)
                && group.All(g => !g.IncludeDescendants)
                && plan.Measures.All(m => m.Aggregation == ReportAggregationKind.Sum);

            if (completeAdditiveGroups)
            {
                var totals = new ReportSubtotalAccumulator(plan.Measures);
                foreach (var row in rawRows.Rows)
                {
                    totals.Add(row.Values);
                }

                grand = new ReportDataRow(plan.Measures.ToDictionary(
                    m => m.OutputCode,
                    m =>
                    {
                        totals.TryGetValue(m.OutputCode, out var value);
                        return value;
                    }));
            }
            else
            {
                grand = (await ReadAsync(grandPlan, new(0, 1), null, ct)).Rows.SingleOrDefault();
            }

            if (pivot && columnRows.Length > 0)
            {
                var totalCells = await ReadAsync(
                    grandPlan with
                    {
                        ColumnGroups = plan.ColumnGroups
                    },
                    new(0, columnRows.Length),
                    null,
                    ct);

                if (totalCells.HasMore)
                    throw new NgbInvariantViolationException("Pivot column axis changed within a read session.");

                grandCells = totalCells.Rows;
            }
        }

        if (pivot)
        {
            columnRows = (await ReportEngine.EnrichInteractiveFieldsAsync(plan with { RowGroups = [], DetailFields = [] },
                new([], columnRows, 0, columnRows.Length, null, false), documentDisplays, ct)).Rows.ToArray();
            cells = (await ReportEngine.EnrichInteractiveFieldsAsync(plan,
                new([], cells, 0, cells.Count, null, false), documentDisplays, ct)).Rows;
            grandCells = (await ReportEngine.EnrichInteractiveFieldsAsync(plan with { RowGroups = [], DetailFields = [] },
                new([], grandCells, 0, grandCells.Count, null, false), documentDisplays, ct)).Rows;
        }
        
        var formatter = new ReportCellFormatter();
        var actions = new ReportComposableCellActionResolver(plan, definition.Dataset);
        ReportSheetDto sheet;
        
        if (pivot)
        {
            var renderer = new ReportPivotMatrixBuilder(formatter, new(formatter, actions), actions);
            var result = renderer.BuildPage(
                plan,
                rows.Rows,
                columnRows,
                cells,
                grand,
                grandCells,
                showGroupValues,
                separateSubtotals);

            sheet = new(
                result.Columns,
                result.Rows,
                new(
                    Title: definition.Definition.Name,
                    IsPivot: true,
                    HasColumnGroups: true),
                result.HeaderRows);
        }
        else
        {
            var columns = ReportSheetBuilder.BuildColumns(plan);
            var rendered = new List<ReportSheetRowDto>();

            foreach (var row in rows.Rows)
            {
                var values = row.Values;
                rendered.Add(new(
                    group.Length == 0
                        ? ReportRowKind.Detail 
                        : ReportRowKind.Group,
                    columns.Select(column => ReportRowHierarchy.IsHierarchyColumn(column)
                        ? formatter.BuildLabelCell(ReportRowHierarchy.FormatGroupLabel(
                            formatter, group[0],
                            values.GetValueOrDefault(group[0].OutputCode)),
                            semanticRole: "group",
                            action: actions.ResolveForGroup(group[0], values))
                        : group.Length > 0 && !showGroupValues
                            ? new ReportCellDto(Value: null, Display: "", ValueType: column.DataType)
                            : formatter.BuildCell(
                                    values.GetValueOrDefault(column.Code),
                                    column,
                                    action: actions.ResolveForDetailColumn(column.Code, values)))
                                .ToArray(),
                    OutlineLevel: 0));

                if (separateSubtotals)
                {
                    var totals = new ReportSubtotalBuilder(formatter);
                    var accumulator = totals.CreateAccumulator(plan.Measures);
                    accumulator.Add(values);
                    var label = ReportRowHierarchy.FormatGroupLabel(formatter, group[0], values.GetValueOrDefault(group[0].OutputCode));

                    rendered.Add(totals.BuildSummaryRow(
                        columns,
                        accumulator,
                        label + " subtotal",
                        ReportRowKind.Subtotal,
                        0,
                        label,
                        "subtotal"));
                }
            }

            if (grand is not null && (group.Length > 0 || details.Count > 0))
            {
                var subtotals = new ReportSubtotalBuilder(formatter);
                var accumulator = subtotals.CreateAccumulator(plan.Measures);
                accumulator.Add(grand.Values);

                rendered.Add(subtotals.BuildSummaryRow(
                    columns,
                    accumulator,
                    "Total",
                    ReportRowKind.Total,
                    0,
                    "grand-total",
                    "grand-total"));
            }

            sheet = new(
                columns,
                rendered,
                new(Title: definition.Definition.Name, HasRowOutline: group.Length > 0));
        }

        if (group.Length > 0 && (path.Count + 1 < original.RowGroups.Count || original.DetailFields.Count > 0))
        {
            var index = 0;
            sheet = sheet with
            { 
                Rows = sheet.Rows.Select(r => r.RowKind != ReportRowKind.Group
                    ? r
                    : r with
                    {
                        ChildrenPath = path.Append(JsonSerializer.SerializeToElement(rawRows.Rows[index++].Values.GetValueOrDefault(group[0].OutputCode))).ToArray()
                    })
                    .ToArray()
            };
        }

        var diagnostics = new Dictionary<string, string>(rows.Diagnostics ?? new Dictionary<string, string>())
        {
            ["paging"] = "query",
            ["groupDepth"] = path.Count.ToString()
        };

        return new(sheet, 0, pageSize, null, rows.HasMore, rows.NextCursor, diagnostics);
    }

    private Task<ReportDataPage> ReadAsync(
        ReportQueryPlan plan,
        ReportPlanPaging paging,
        ReportRowSelection? selection,
        CancellationToken ct)
        => source.ReadPageAsync(
            new(plan.ReportCode, plan.DatasetCode, ReportEngine.MapGroups(plan.RowGroups),
            ReportEngine.MapGroups(plan.ColumnGroups),
            ReportEngine.MapFields(plan.DetailFields),
            ReportEngine.MapMeasures(plan.Measures),
            ReportEngine.MapSorts(plan.Sorts),
            ReportEngine.MapPredicates(plan.Predicates),
            ReportEngine.MapParameters(plan.Parameters)),
            paging,
            selection,
            ct);

    private static Exception TooWide(int max)
        => new NgbArgumentInvalidException("layout.columnGroups", $"The pivot exceeds {max} columns. Narrow the column groups or filters.");
}
