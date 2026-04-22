using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Rendering;

internal sealed class ReportGroupTreeBuilder(
    ReportCellFormatter cellFormatter,
    ReportSubtotalBuilder subtotalBuilder,
    ReportComposableCellActionResolver actionResolver)
{
    private readonly ReportCellFormatter _cellFormatter = cellFormatter ?? throw new NgbArgumentRequiredException(nameof(cellFormatter));
    private readonly ReportSubtotalBuilder _subtotalBuilder = subtotalBuilder ?? throw new NgbArgumentRequiredException(nameof(subtotalBuilder));
    private readonly ReportComposableCellActionResolver _actionResolver = actionResolver ?? throw new NgbArgumentRequiredException(nameof(actionResolver));

    public IReadOnlyList<ReportSheetRowDto> BuildRows(
        ReportQueryPlan plan,
        IReadOnlyList<ReportSheetColumnDto> columns,
        IReadOnlyList<ReportDataRow> dataRows)
    {
        if (plan is null)
            throw new NgbArgumentRequiredException(nameof(plan));

        if (columns is null)
            throw new NgbArgumentRequiredException(nameof(columns));

        if (dataRows is null)
            throw new NgbArgumentRequiredException(nameof(dataRows));

        if (dataRows.Count == 0)
            return [];

        return plan.RowGroups.Count == 0
            ? BuildFlatRows(plan, columns, dataRows)
            : BuildGroupedRows(plan, columns, dataRows);
    }

    private IReadOnlyList<ReportSheetRowDto> BuildFlatRows(
        ReportQueryPlan plan,
        IReadOnlyList<ReportSheetColumnDto> columns,
        IReadOnlyList<ReportDataRow> dataRows)
    {
        var rows = new List<ReportSheetRowDto>();
        var hasMeasures = plan.Measures.Count > 0;
        var grandTotals = _subtotalBuilder.CreateAccumulator(plan.Measures);

        foreach (var dataRow in dataRows)
        {
            if (hasMeasures)
                _subtotalBuilder.Add(grandTotals, dataRow.Values);
        }

        var isPureGrandTotal = hasMeasures && plan.DetailFields.Count == 0 && dataRows.Count == 1;
        if (isPureGrandTotal)
        {
            if (plan.Shape.ShowGrandTotals)
            {
                rows.Add(_subtotalBuilder.BuildSummaryRow(
                    columns,
                    grandTotals,
                    label: "Total",
                    rowKind: ReportRowKind.Total,
                    outlineLevel: 0,
                    groupKey: "grand-total",
                    semanticRole: "grand-total"));
            }
            else
            {
                var onlyRow = dataRows[0];
                rows.Add(new ReportSheetRowDto(
                    RowKind: ReportRowKind.Detail,
                    Cells: columns
                        .Select(col => _cellFormatter.BuildCell(onlyRow.Values.GetValueOrDefault(col.Code), col, semanticRole: "detail", action: _actionResolver.ResolveForDetailColumn(col.Code, onlyRow.Values)))
                        .ToList(),
                    OutlineLevel: 0,
                    GroupKey: null,
                    SemanticRole: "detail"));
            }

            return rows;
        }

        foreach (var dataRow in dataRows)
        {
            rows.Add(new ReportSheetRowDto(
                RowKind: ReportRowKind.Detail,
                Cells: columns
                    .Select(col => _cellFormatter.BuildCell(dataRow.Values.GetValueOrDefault(col.Code), col, semanticRole: "detail", action: _actionResolver.ResolveForDetailColumn(col.Code, dataRow.Values)))
                    .ToList(),
                OutlineLevel: 0,
                GroupKey: null,
                SemanticRole: "detail"));
        }

        if (hasMeasures && plan.Shape.ShowGrandTotals)
        {
            rows.Add(_subtotalBuilder.BuildSummaryRow(
                columns,
                grandTotals,
                label: "Total",
                rowKind: ReportRowKind.Total,
                outlineLevel: 0,
                groupKey: "grand-total",
                semanticRole: "grand-total"));
        }

        return rows;
    }

    private IReadOnlyList<ReportSheetRowDto> BuildGroupedRows(
        ReportQueryPlan plan,
        IReadOnlyList<ReportSheetColumnDto> columns,
        IReadOnlyList<ReportDataRow> dataRows)
    {
        var rows = new List<ReportSheetRowDto>();
        var grandTotals = _subtotalBuilder.CreateAccumulator(plan.Measures);
        var openGroups = new List<OpenGroupState>();
        var hasDetailRows = plan.Shape.ShowDetails || plan.DetailFields.Count > 0;
        ReportDataRow? previous = null;

        foreach (var dataRow in dataRows)
        {
            var firstDifferentLevel = previous is null
                ? 0
                : FindFirstDifferentLevel(plan.RowGroups, previous.Values, dataRow.Values);

            CloseGroups(rows, columns, plan, openGroups, firstDifferentLevel, hasDetailRows);

            for (var level = firstDifferentLevel; level < plan.RowGroups.Count; level++)
            {
                var grouping = plan.RowGroups[level];
                var groupValue = dataRow.Values.GetValueOrDefault(grouping.OutputCode);
                var state = new OpenGroupState(
                    Level: level,
                    Grouping: grouping,
                    GroupValue: groupValue,
                    GroupKey: BuildGroupKey(plan.RowGroups, level, dataRow.Values),
                    Totals: _subtotalBuilder.CreateAccumulator(plan.Measures),
                    RowIndex: rows.Count,
                    SourceValues: dataRow.Values);

                openGroups.Add(state);
                rows.Add(BuildGroupRow(columns, plan, state, totals: null, hasDetailRows));
            }

            _subtotalBuilder.Add(grandTotals, dataRow.Values);
            foreach (var state in openGroups)
                _subtotalBuilder.Add(state.Totals, dataRow.Values);

            if (hasDetailRows)
                rows.Add(BuildDetailRow(columns, plan, dataRow.Values, openGroups[^1].GroupKey));

            previous = dataRow;
        }

        CloseGroups(rows, columns, plan, openGroups, 0, hasDetailRows);

        if (plan.Measures.Count > 0 && plan.Shape.ShowGrandTotals)
        {
            rows.Add(_subtotalBuilder.BuildSummaryRow(
                columns,
                grandTotals,
                label: "Total",
                rowKind: ReportRowKind.Total,
                outlineLevel: 0,
                groupKey: "grand-total",
                semanticRole: "grand-total"));
        }

        return rows;
    }

    private void CloseGroups(
        IList<ReportSheetRowDto> rows,
        IReadOnlyList<ReportSheetColumnDto> columns,
        ReportQueryPlan plan,
        IList<OpenGroupState> openGroups,
        int keepCount,
        bool hasDetailRows)
    {
        for (var index = openGroups.Count - 1; index >= keepCount; index--)
        {
            var state = openGroups[index];
            if (ShouldInlineGroupTotals(plan, state.Level, hasDetailRows))
            {
                rows[state.RowIndex] = BuildGroupRow(columns, plan, state, state.Totals, hasDetailRows);
            }
            else if (ShouldEmitSeparateSubtotal(plan, state.Level))
            {
                rows.Add(BuildSubtotalRow(columns, state));
            }

            openGroups.RemoveAt(index);
        }
    }

    private ReportSheetRowDto BuildGroupRow(
        IReadOnlyList<ReportSheetColumnDto> columns,
        ReportQueryPlan plan,
        OpenGroupState state,
        ReportSubtotalAccumulator? totals,
        bool hasDetailRows)
    {
        var inlineGroupTotals = totals is not null && ShouldInlineGroupTotals(plan, state.Level, hasDetailRows);
        var cells = new List<ReportCellDto>(columns.Count);

        foreach (var column in columns)
        {
            if (ReportRowHierarchy.IsHierarchyColumn(column))
            {
                cells.Add(_cellFormatter.BuildLabelCell(
                    ReportRowHierarchy.FormatGroupLabel(_cellFormatter, state.Grouping, state.GroupValue),
                    styleKey: "group",
                    semanticRole: "group",
                    action: _actionResolver.ResolveForGroup(state.Grouping, state.SourceValues)));
                continue;
            }

            if (inlineGroupTotals && totals!.TryGetValue(column.Code, out var totalValue))
            {
                cells.Add(_cellFormatter.BuildCell(totalValue, column, styleKey: "group", semanticRole: "group-measure"));
                continue;
            }

            cells.Add(_cellFormatter.BuildBlankCell(column, styleKey: "group", semanticRole: "group"));
        }

        return new ReportSheetRowDto(
            RowKind: ReportRowKind.Group,
            Cells: cells,
            OutlineLevel: state.Level,
            GroupKey: state.GroupKey,
            SemanticRole: "group");
    }

    private ReportSheetRowDto BuildDetailRow(
        IReadOnlyList<ReportSheetColumnDto> columns,
        ReportQueryPlan plan,
        IReadOnlyDictionary<string, object?> values,
        string groupKey)
    {
        var detailCodes = new HashSet<string>(plan.DetailFields.Select(x => x.OutputCode), StringComparer.OrdinalIgnoreCase);
        var measureCodes = new HashSet<string>(plan.Measures.Select(x => x.OutputCode), StringComparer.OrdinalIgnoreCase);
        var cells = new List<ReportCellDto>(columns.Count);

        foreach (var column in columns)
        {
            if (detailCodes.Contains(column.Code) || measureCodes.Contains(column.Code))
            {
                cells.Add(_cellFormatter.BuildCell(values.GetValueOrDefault(column.Code), column, semanticRole: "detail", action: _actionResolver.ResolveForDetailColumn(column.Code, values)));
                continue;
            }

            cells.Add(_cellFormatter.BuildBlankCell(column, semanticRole: "detail"));
        }

        return new ReportSheetRowDto(
            RowKind: ReportRowKind.Detail,
            Cells: cells,
            OutlineLevel: plan.RowGroups.Count,
            GroupKey: groupKey,
            SemanticRole: "detail");
    }

    private ReportSheetRowDto BuildSubtotalRow(
        IReadOnlyList<ReportSheetColumnDto> columns,
        OpenGroupState state)
        => _subtotalBuilder.BuildSummaryRow(
            columns,
            state.Totals,
            label: $"{ReportRowHierarchy.FormatGroupLabel(_cellFormatter, state.Grouping, state.GroupValue)} subtotal",
            rowKind: ReportRowKind.Subtotal,
            outlineLevel: state.Level,
            groupKey: state.GroupKey,
            semanticRole: "subtotal");

    private static bool ShouldInlineGroupTotals(ReportQueryPlan plan, int level, bool hasDetailRows)
    {
        if (plan.Measures.Count == 0)
            return false;

        if (plan.Shape.ShowSubtotals)
            return !plan.Shape.ShowSubtotalsOnSeparateRows || level == plan.RowGroups.Count - 1;

        return !hasDetailRows && level == plan.RowGroups.Count - 1;
    }

    private static bool ShouldEmitSeparateSubtotal(ReportQueryPlan plan, int level)
        => plan.Measures.Count > 0
           && plan.Shape is { ShowSubtotals: true, ShowSubtotalsOnSeparateRows: true }
           && level < plan.RowGroups.Count - 1;

    private static int FindFirstDifferentLevel(
        IReadOnlyList<Planning.ReportPlanGrouping> rowGroups,
        IReadOnlyDictionary<string, object?> previous,
        IReadOnlyDictionary<string, object?> current)
    {
        for (var level = 0; level < rowGroups.Count; level++)
        {
            var outputCode = rowGroups[level].OutputCode;
            var previousValue = previous.GetValueOrDefault(outputCode);
            var currentValue = current.GetValueOrDefault(outputCode);
            if (!Equals(previousValue, currentValue))
                return level;
        }

        return rowGroups.Count;
    }

    private string BuildGroupKey(
        IReadOnlyList<Planning.ReportPlanGrouping> rowGroups,
        int level,
        IReadOnlyDictionary<string, object?> values)
    {
        var segments = new List<string>(level + 1);
        for (var index = 0; index <= level; index++)
        {
            var grouping = rowGroups[index];
            var value = values.GetValueOrDefault(grouping.OutputCode);
            segments.Add($"{grouping.OutputCode}={_cellFormatter.FormatGroupLabel(value)}");
        }

        return string.Join("|", segments);
    }

    private sealed record OpenGroupState(
        int Level,
        Planning.ReportPlanGrouping Grouping,
        object? GroupValue,
        string GroupKey,
        ReportSubtotalAccumulator Totals,
        int RowIndex,
        IReadOnlyDictionary<string, object?> SourceValues);
}
