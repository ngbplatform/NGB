using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Rendering;

internal sealed class ReportPivotMatrixBuilder(
    ReportCellFormatter cellFormatter,
    ReportPivotHeaderBuilder headerBuilder,
    ReportComposableCellActionResolver actionResolver)
{
    private readonly ReportCellFormatter _cellFormatter = cellFormatter
        ?? throw new NgbArgumentRequiredException(nameof(cellFormatter));

    private readonly ReportPivotHeaderBuilder _headerBuilder = headerBuilder
        ?? throw new NgbArgumentRequiredException(nameof(headerBuilder));

    private readonly ReportComposableCellActionResolver _actionResolver = actionResolver
        ?? throw new NgbArgumentRequiredException(nameof(actionResolver));

    public ReportPivotSheetBuildResult Build(
        ReportDefinitionRuntimeModel definition,
        ReportQueryPlan plan,
        ReportDataPage page)
    {
        if (definition is null)
            throw new NgbArgumentRequiredException(nameof(definition));

        if (plan is null)
            throw new NgbArgumentRequiredException(nameof(plan));

        if (page is null)
            throw new NgbArgumentRequiredException(nameof(page));

        if (plan.ColumnGroups.Count == 0)
            throw new NgbInvariantViolationException("Pivot matrix builder requires at least one column group.");

        var rowAxisColumns = BuildRowAxisColumns(plan);
        var rowAxisValueCodes = ReportRowHierarchy.BuildValueCodes(plan);
        var rowGroupCodes = plan.RowGroups.Select(x => x.OutputCode).ToList();
        var columnGroupCodes = plan.ColumnGroups.Select(x => x.OutputCode).ToList();
        var includeTotals = plan.Shape.ShowGrandTotals && plan.Measures.Count > 0;

        var leafRowsByKey = new Dictionary<string, PivotLeafRow>(StringComparer.OrdinalIgnoreCase);
        var leafRowOrder = new List<PivotLeafRow>();
        var columnLeavesByKey = new Dictionary<string, PivotColumnLeaf>(StringComparer.OrdinalIgnoreCase);
        var columnLeafOrder = new List<PivotColumnLeaf>();

        foreach (var dataRow in page.Rows)
        {
            var rowKey = BuildTupleKey(rowAxisValueCodes, dataRow.Values);
            if (!leafRowsByKey.TryGetValue(rowKey, out var leafRow))
            {
                leafRow = new PivotLeafRow(
                    rowKey,
                    rowAxisValueCodes
                        .ToDictionary(x => x, x => dataRow.Values.GetValueOrDefault(x), StringComparer.OrdinalIgnoreCase),
                    rowGroupCodes
                        .Select(x => dataRow.Values.GetValueOrDefault(x))
                        .ToList(),
                    dataRow.Values);
                leafRowsByKey[rowKey] = leafRow;
                leafRowOrder.Add(leafRow);
            }

            var columnKey = BuildTupleKey(columnGroupCodes, dataRow.Values);
            if (!columnLeavesByKey.TryGetValue(columnKey, out var columnLeaf))
            {
                columnLeaf = new PivotColumnLeaf(
                    columnKey,
                    columnGroupCodes.Select(x => dataRow.Values.GetValueOrDefault(x)).ToList(),
                    plan.ColumnGroups
                        .Select(x => _cellFormatter.FormatGroupLabel(
                            dataRow.Values.GetValueOrDefault(x.OutputCode),
                            x.TimeGrain))
                        .ToList(),
                    new Dictionary<string, object?>(dataRow.Values, StringComparer.OrdinalIgnoreCase));
                columnLeavesByKey[columnKey] = columnLeaf;
                columnLeafOrder.Add(columnLeaf);
            }

            foreach (var measure in plan.Measures)
            {
                var rawValue = dataRow.Values.GetValueOrDefault(measure.OutputCode);
                leafRow.AddValue(columnLeaf.Key, measure, rawValue);
            }
        }

        var valueColumns = BuildValueColumns(plan, columnLeafOrder, includeTotals);
        var allColumns = rowAxisColumns.Concat(valueColumns.Select(x => x.Column)).ToList();
        var headerRows = _headerBuilder.Build(rowAxisColumns, plan.ColumnGroups, columnLeafOrder, plan.Measures, includeTotals);
        var bodyRows = BuildBodyRows(plan, rowAxisColumns, valueColumns, leafRowOrder, includeTotals);

        EnsureCaps(definition, plan, allColumns.Count, bodyRows.Count + headerRows.Count, columnLeafOrder.Count, includeTotals);

        var diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sheetBuilder"] = "pivot-v1",
            ["pivotColumnLeafCount"] = columnLeafOrder.Count.ToString(),
            ["pivotValueColumnCount"] = valueColumns.Count.ToString(),
            ["pivotHeaderRows"] = headerRows.Count.ToString()
        };

        return new ReportPivotSheetBuildResult(allColumns, headerRows, bodyRows, diagnostics);
    }

    private IReadOnlyList<ReportSheetRowDto> BuildBodyRows(
        ReportQueryPlan plan,
        IReadOnlyList<ReportSheetColumnDto> rowAxisColumns,
        IReadOnlyList<PivotValueColumn> valueColumns,
        IReadOnlyList<PivotLeafRow> leafRows,
        bool includeTotals)
    {
        var rows = new List<ReportSheetRowDto>();
        var hasDetailRows = plan.DetailFields.Count > 0;

        if (leafRows.Count == 0)
            return rows;

        if (plan.RowGroups.Count == 0)
        {
            rows.AddRange(leafRows.Select(row => BuildLeafRow(plan, rowAxisColumns, valueColumns, row, includeTotals, hasDetailRows: false)));
        }
        else
        {
            EmitGroupLevel(rows, plan, rowAxisColumns, valueColumns, leafRows, includeTotals, level: 0, hasDetailRows: hasDetailRows);
        }

        if (plan.Measures.Count > 0 && plan.Shape.ShowGrandTotals)
            rows.Add(BuildGrandTotalRow(plan, rowAxisColumns, valueColumns, leafRows, includeTotals));

        return rows;
    }

    private void EmitGroupLevel(
        List<ReportSheetRowDto> rows,
        ReportQueryPlan plan,
        IReadOnlyList<ReportSheetColumnDto> rowAxisColumns,
        IReadOnlyList<PivotValueColumn> valueColumns,
        IReadOnlyList<PivotLeafRow> groupRows,
        bool includeTotals,
        int level,
        bool hasDetailRows)
    {
        var index = 0;
        while (index < groupRows.Count)
        {
            var prefixValue = groupRows[index].RowGroupValues[level];
            var end = index + 1;

            while (end < groupRows.Count && Equals(groupRows[end].RowGroupValues[level], prefixValue))
            {
                end++;
            }

            var slice = groupRows.Skip(index).Take(end - index).ToList();
            rows.Add(BuildGroupRow(plan, rowAxisColumns, valueColumns, slice, level, includeTotals, hasDetailRows));

            if (level < plan.RowGroups.Count - 1)
            {
                EmitGroupLevel(rows, plan, rowAxisColumns, valueColumns, slice, includeTotals, level + 1, hasDetailRows);
            }
            else if (hasDetailRows)
            {
                rows.AddRange(slice.Select(row => BuildLeafRow(plan, rowAxisColumns, valueColumns, row, includeTotals, hasDetailRows: true)));
            }

            if (ShouldEmitSeparateSubtotal(plan, level))
                rows.Add(BuildSubtotalRow(plan, rowAxisColumns, valueColumns, slice, level, includeTotals));

            index = end;
        }
    }

    private ReportSheetRowDto BuildLeafRow(
        ReportQueryPlan plan,
        IReadOnlyList<ReportSheetColumnDto> rowAxisColumns,
        IReadOnlyList<PivotValueColumn> valueColumns,
        PivotLeafRow leafRow,
        bool includeTotals,
        bool hasDetailRows)
    {
        var cells = new List<ReportCellDto>(rowAxisColumns.Count + valueColumns.Count);
        foreach (var rowAxisColumn in rowAxisColumns)
        {
            if (ReportRowHierarchy.IsHierarchyColumn(rowAxisColumn))
            {
                cells.Add(hasDetailRows
                    ? _cellFormatter.BuildBlankCell(rowAxisColumn, semanticRole: rowAxisColumn.SemanticRole)
                    : _cellFormatter.BuildLabelCell(
                        ReportRowHierarchy.FormatLeafLabel(_cellFormatter, plan.RowGroups, leafRow.RowGroupValues),
                        semanticRole: rowAxisColumn.SemanticRole,
                        action: plan.RowGroups.Count == 0 
                            ? null 
                            : _actionResolver.ResolveForGroup(plan.RowGroups[^1], leafRow.SourceValues)));
                continue;
            }

            cells.Add(_cellFormatter.BuildCell(leafRow.RowAxisValues.GetValueOrDefault(rowAxisColumn.Code), rowAxisColumn, semanticRole: rowAxisColumn.SemanticRole, action: _actionResolver.ResolveForDetailColumn(rowAxisColumn.Code, leafRow.SourceValues)));
        }

        foreach (var valueColumn in valueColumns)
        {
            var value = valueColumn.IsTotal
                ? leafRow.GetTotal(valueColumn.Measure)
                : leafRow.GetValue(valueColumn.Leaf!.Key, valueColumn.Measure.OutputCode);
            cells.Add(_cellFormatter.BuildCell(value, valueColumn.Column, semanticRole: valueColumn.Column.SemanticRole));
        }

        var outlineLevel = hasDetailRows
            ? plan.RowGroups.Count
            : Math.Max(0, plan.RowGroups.Count - 1);

        return new ReportSheetRowDto(
            RowKind: ReportRowKind.Detail,
            Cells: cells,
            OutlineLevel: outlineLevel,
            GroupKey: leafRow.Key,
            SemanticRole: "pivot-detail");
    }

    private ReportSheetRowDto BuildGroupRow(
        ReportQueryPlan plan,
        IReadOnlyList<ReportSheetColumnDto> rowAxisColumns,
        IReadOnlyList<PivotValueColumn> valueColumns,
        IReadOnlyList<PivotLeafRow> leafRows,
        int level,
        bool includeTotals,
        bool hasDetailRows)
    {
        var cells = new List<ReportCellDto>(rowAxisColumns.Count + valueColumns.Count);
        var currentValue = leafRows[0].RowGroupValues[level];
        var currentLabel = _cellFormatter.FormatGroupLabel(currentValue, plan.RowGroups[level].TimeGrain);
        var inlineGroupTotals = ShouldInlineGroupTotals(plan, level, hasDetailRows);

        foreach (var rowAxisColumn in rowAxisColumns)
        {
            if (ReportRowHierarchy.IsHierarchyColumn(rowAxisColumn))
            {
                cells.Add(_cellFormatter.BuildLabelCell(currentLabel, styleKey: "group", semanticRole: "group", action: _actionResolver.ResolveForGroup(plan.RowGroups[level], leafRows[0].SourceValues)));
                continue;
            }

            cells.Add(_cellFormatter.BuildBlankCell(rowAxisColumn, styleKey: "group", semanticRole: "group"));
        }

        foreach (var valueColumn in valueColumns)
        {
            if (!inlineGroupTotals)
            {
                cells.Add(_cellFormatter.BuildBlankCell(valueColumn.Column, styleKey: "group", semanticRole: "group"));
                continue;
            }

            var value = valueColumn.IsTotal
                ? SumLeafRows(leafRows, valueColumn.Measure, columnLeaf: null)
                : SumLeafRows(leafRows, valueColumn.Measure, valueColumn.Leaf);
            cells.Add(_cellFormatter.BuildCell(value, valueColumn.Column, styleKey: "group", semanticRole: "group-measure"));
        }

        return new ReportSheetRowDto(
            RowKind: ReportRowKind.Group,
            Cells: cells,
            OutlineLevel: level,
            GroupKey: $"group:{level}:{leafRows[0].Key}",
            SemanticRole: "group");
    }

    private ReportSheetRowDto BuildGrandTotalRow(
        ReportQueryPlan plan,
        IReadOnlyList<ReportSheetColumnDto> rowAxisColumns,
        IReadOnlyList<PivotValueColumn> valueColumns,
        IReadOnlyList<PivotLeafRow> leafRows,
        bool includeTotals)
    {
        var cells = new List<ReportCellDto>(rowAxisColumns.Count + valueColumns.Count);
        for (var columnIndex = 0; columnIndex < rowAxisColumns.Count; columnIndex++)
        {
            var rowAxisColumn = rowAxisColumns[columnIndex];
            cells.Add(columnIndex == 0
                ? _cellFormatter.BuildLabelCell("Total", styleKey: "grand-total", semanticRole: "grand-total")
                : _cellFormatter.BuildBlankCell(rowAxisColumn, styleKey: "grand-total", semanticRole: "grand-total"));
        }

        foreach (var valueColumn in valueColumns)
        {
            var value = valueColumn.IsTotal
                ? SumLeafRows(leafRows, valueColumn.Measure, columnLeaf: null)
                : SumLeafRows(leafRows, valueColumn.Measure, valueColumn.Leaf);
            cells.Add(_cellFormatter.BuildCell(value, valueColumn.Column, styleKey: "grand-total", semanticRole: "grand-total"));
        }

        return new ReportSheetRowDto(
            RowKind: ReportRowKind.Total,
            Cells: cells,
            OutlineLevel: 0,
            GroupKey: "grand-total",
            SemanticRole: "grand-total");
    }

    private IReadOnlyList<PivotValueColumn> BuildValueColumns(
        ReportQueryPlan plan,
        IReadOnlyList<PivotColumnLeaf> columnLeaves,
        bool includeTotals)
    {
        var columns = new List<PivotValueColumn>();
        for (var leafIndex = 0; leafIndex < columnLeaves.Count; leafIndex++)
        {
            var leaf = columnLeaves[leafIndex];
            foreach (var measure in plan.Measures)
            {
                var title = plan.Measures.Count == 1
                    ? string.Join(" / ", leaf.Labels)
                    : measure.Label;
                columns.Add(new PivotValueColumn(
                    new ReportSheetColumnDto(
                        Code: $"pivot_{leafIndex}_{measure.OutputCode}",
                        Title: title,
                        DataType: measure.DataType,
                        SemanticRole: "pivot-measure"),
                    leaf,
                    measure,
                    IsTotal: false));
            }
        }

        if (includeTotals)
        {
            foreach (var measure in plan.Measures)
            {
                var title = plan.Measures.Count == 1 ? "Total" : $"Total {measure.Label}";
                columns.Add(new PivotValueColumn(
                    new ReportSheetColumnDto(
                        Code: $"total_{measure.OutputCode}",
                        Title: title,
                        DataType: measure.DataType,
                        SemanticRole: "pivot-total"),
                    Leaf: null,
                    Measure: measure,
                    IsTotal: true));
            }
        }

        return columns;
    }

    private IReadOnlyList<ReportSheetColumnDto> BuildRowAxisColumns(ReportQueryPlan plan)
    {
        var columns = new List<ReportSheetColumnDto>();

        if (ReportRowHierarchy.HasHierarchy(plan.RowGroups))
            columns.Add(ReportRowHierarchy.CreateColumn(plan.RowGroups));

        foreach (var detailField in plan.DetailFields)
        {
            columns.Add(new ReportSheetColumnDto(
                detailField.OutputCode,
                detailField.Label,
                detailField.DataType,
                SemanticRole: "detail"));
        }

        return columns;
    }

    private void EnsureCaps(
        ReportDefinitionRuntimeModel definition,
        ReportQueryPlan plan,
        int totalColumns,
        int totalRowsIncludingHeaders,
        int pivotLeafCount,
        bool includeTotals)
    {
        var caps = definition.Capabilities;
        if (caps.MaxVisibleColumns is { } maxColumns && totalColumns > maxColumns)
        {
            throw Invalid(
                definition,
                "layout.columnGroups",
                $"This report would display {totalColumns} columns, which exceeds the limit of {maxColumns}. Reduce the number of column groups, measures, or filters and try again.");
        }

        if (caps.MaxVisibleRows is { } maxRows && totalRowsIncludingHeaders > maxRows)
        {
            throw Invalid(
                definition,
                ResolveVisibleRowFieldPath(plan),
                $"This report would display {totalRowsIncludingHeaders} rows, which exceeds the limit of {maxRows}. Narrow the filters or reduce the number of groups and try again.");
        }

        var renderedCells = totalColumns * totalRowsIncludingHeaders;
        if (caps.MaxRenderedCells is { } maxCells && renderedCells > maxCells)
        {
            throw Invalid(
                definition,
                "layout.columnGroups",
                $"This report would display {renderedCells} cells, which exceeds the limit of {maxCells}. Narrow the filters or reduce the number of groups and try again.");
        }

        if (caps.MaxVisibleColumns is { } pivotCap)
        {
            var pivotValueColumns = pivotLeafCount * Math.Max(1, plan.Measures.Count) + (includeTotals ? plan.Measures.Count : 0);
            if (pivotValueColumns > pivotCap)
            {
                throw Invalid(
                    definition,
                    "layout.columnGroups",
                    $"This report would display {pivotValueColumns} pivot value columns, which exceeds the limit of {pivotCap}. Reduce the number of column groups, measures, or filters and try again.");
            }
        }
    }

    private static string ResolveVisibleRowFieldPath(ReportQueryPlan plan)
        => plan.RowGroups.Count > 0 || plan.DetailFields.Count > 0
            ? "layout.rowGroups"
            : plan.ColumnGroups.Count > 0
                ? "layout.columnGroups"
                : "layout.measures";

    private static string BuildTupleKey(IReadOnlyList<string> codes, IReadOnlyDictionary<string, object?> values)
        => codes.Count == 0
            ? "(all)"
            : string.Join("|", codes.Select(code => $"{code}={values.GetValueOrDefault(code) ?? "<null>"}"));

    private ReportSheetRowDto BuildSubtotalRow(
        ReportQueryPlan plan,
        IReadOnlyList<ReportSheetColumnDto> rowAxisColumns,
        IReadOnlyList<PivotValueColumn> valueColumns,
        IReadOnlyList<PivotLeafRow> leafRows,
        int level,
        bool includeTotals)
    {
        var cells = new List<ReportCellDto>(rowAxisColumns.Count + valueColumns.Count);
        var currentValue = leafRows[0].RowGroupValues[level];
        var currentLabel = _cellFormatter.FormatGroupLabel(currentValue, plan.RowGroups[level].TimeGrain) + " subtotal";

        foreach (var rowAxisColumn in rowAxisColumns)
        {
            cells.Add(ReportRowHierarchy.IsHierarchyColumn(rowAxisColumn)
                ? _cellFormatter.BuildLabelCell(currentLabel, styleKey: "subtotal", semanticRole: "subtotal")
                : _cellFormatter.BuildBlankCell(rowAxisColumn, styleKey: "subtotal", semanticRole: "subtotal"));
        }

        foreach (var valueColumn in valueColumns)
        {
            var value = valueColumn.IsTotal
                ? SumLeafRows(leafRows, valueColumn.Measure, columnLeaf: null)
                : SumLeafRows(leafRows, valueColumn.Measure, valueColumn.Leaf);
            cells.Add(_cellFormatter.BuildCell(value, valueColumn.Column, styleKey: "subtotal", semanticRole: "subtotal"));
        }

        return new ReportSheetRowDto(
            RowKind: ReportRowKind.Subtotal,
            Cells: cells,
            OutlineLevel: level,
            GroupKey: $"subtotal:{level}:{leafRows[0].Key}",
            SemanticRole: "subtotal");
    }

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

    private static object? SumLeafRows(
        IReadOnlyList<PivotLeafRow> leafRows,
        Planning.ReportPlanMeasure measure,
        PivotColumnLeaf? columnLeaf)
    {
        object? total = null;
        foreach (var leafRow in leafRows)
        {
            var value = columnLeaf is null
                ? leafRow.GetTotal(measure)
                : leafRow.GetValue(columnLeaf.Key, measure.OutputCode);
            total = AddValues(measure, total, value);
        }

        return total;
    }

    private static object? AddValues(Planning.ReportPlanMeasure measure, object? existing, object? raw)
    {
        if (raw is null)
            return existing;

        return measure.DataType switch
        {
            "int64" => ConvertToInt64(existing) + ConvertToInt64(raw),
            _ => ConvertToDecimal(existing) + ConvertToDecimal(raw)
        };
    }

    private static long ConvertToInt64(object? value)
        => value switch
        {
            null => 0L,
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            decimal dec => decimal.ToInt64(dec),
            double dbl => Convert.ToInt64(dbl),
            float flt => Convert.ToInt64(flt),
            _ => Convert.ToInt64(value)
        };

    private static decimal ConvertToDecimal(object? value)
        => value switch
        {
            null => 0m,
            decimal dec => dec,
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            double dbl => Convert.ToDecimal(dbl),
            float flt => Convert.ToDecimal(flt),
            _ => Convert.ToDecimal(value)
        };

    private static ReportLayoutValidationException Invalid(
        ReportDefinitionRuntimeModel runtime,
        string fieldPath,
        string message)
        => new(
            message,
            fieldPath,
            errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [fieldPath] = [message]
            },
            context: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["reportCode"] = runtime.ReportCodeNorm
            });
}

internal sealed record PivotColumnLeaf(
    string Key,
    IReadOnlyList<object?> Values,
    IReadOnlyList<string> Labels,
    IReadOnlyDictionary<string, object?> SourceValues);

internal sealed record PivotValueColumn(
    ReportSheetColumnDto Column,
    PivotColumnLeaf? Leaf,
    Planning.ReportPlanMeasure Measure,
    bool IsTotal);

internal sealed class PivotLeafRow(
    string key,
    IReadOnlyDictionary<string, object?> rowAxisValues,
    IReadOnlyList<object?> rowGroupValues,
    IReadOnlyDictionary<string, object?> sourceValues)
{
    private readonly Dictionary<string, object?> _valuesByKey = new(StringComparer.OrdinalIgnoreCase);

    public string Key { get; } = key;
    public IReadOnlyDictionary<string, object?> RowAxisValues { get; } = rowAxisValues;
    public IReadOnlyList<object?> RowGroupValues { get; } = rowGroupValues;
    public IReadOnlyDictionary<string, object?> SourceValues { get; } = sourceValues;

    public void AddValue(string columnLeafKey, Planning.ReportPlanMeasure measure, object? value)
    {
        var compositeKey = BuildCompositeKey(columnLeafKey, measure.OutputCode);
        _valuesByKey[compositeKey] = AddValues(measure, _valuesByKey.GetValueOrDefault(compositeKey), value);
    }

    public object? GetValue(string columnLeafKey, string measureOutputCode)
        => _valuesByKey.GetValueOrDefault(BuildCompositeKey(columnLeafKey, measureOutputCode));

    public object? GetTotal(Planning.ReportPlanMeasure measure)
    {
        object? total = null;
        foreach (var pair in _valuesByKey.Where(x => x.Key.EndsWith("|" + measure.OutputCode, StringComparison.OrdinalIgnoreCase)))
        {
            total = AddValues(measure, total, pair.Value);
        }

        return total;
    }

    private static string BuildCompositeKey(string columnLeafKey, string measureOutputCode)
        => columnLeafKey + "|" + measureOutputCode;

    private static object? AddValues(Planning.ReportPlanMeasure measure, object? existing, object? raw)
        => ReportPivotMatrixBuilderHelper.AddValues(measure, existing, raw);
}

internal sealed record ReportPivotSheetBuildResult(
    IReadOnlyList<ReportSheetColumnDto> Columns,
    IReadOnlyList<ReportSheetRowDto> HeaderRows,
    IReadOnlyList<ReportSheetRowDto> Rows,
    IReadOnlyDictionary<string, string>? Diagnostics = null);

internal static class ReportPivotMatrixBuilderHelper
{
    public static object? AddValues(Planning.ReportPlanMeasure measure, object? existing, object? raw)
    {
        if (raw is null)
            return existing;

        return measure.DataType switch
        {
            "int64" => ConvertToInt64(existing) + ConvertToInt64(raw),
            _ => ConvertToDecimal(existing) + ConvertToDecimal(raw)
        };
    }

    private static long ConvertToInt64(object? value)
        => value switch
        {
            null => 0L,
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            decimal dec => decimal.ToInt64(dec),
            double dbl => Convert.ToInt64(dbl),
            float flt => Convert.ToInt64(flt),
            _ => Convert.ToInt64(value)
        };

    private static decimal ConvertToDecimal(object? value)
        => value switch
        {
            null => 0m,
            decimal dec => dec,
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            double dbl => Convert.ToDecimal(dbl),
            float flt => Convert.ToDecimal(flt),
            _ => Convert.ToDecimal(value)
        };
}
