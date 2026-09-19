using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;

namespace NGB.Runtime.Reporting.Rendering;

internal sealed partial class ReportPivotMatrixBuilder
{
    public ReportPivotSheetBuildResult BuildPage(
        ReportQueryPlan plan,
        IReadOnlyList<ReportDataRow> axisRows,
        IReadOnlyList<ReportDataRow> columnRows,
        IReadOnlyList<ReportDataRow> cells,
        ReportDataRow? grand,
        IReadOnlyList<ReportDataRow> grandCells,
        bool showGroupValues,
        bool separateSubtotals)
    {
        var rowColumns = BuildRowAxisColumns(plan);
        var columnCodes = plan.ColumnGroups.Select(g => g.OutputCode).ToArray();
        var axisCodes = ReportRowHierarchy.BuildValueCodes(plan);
        var leaves = columnRows.Select(row => new PivotColumnLeaf(
                BuildTupleKey(columnCodes, row.Values),
                columnCodes.Select(c => row.Values.GetValueOrDefault(c)).ToArray(),
                plan.ColumnGroups.Select(g => _cellFormatter.FormatGroupLabel(row.Values.GetValueOrDefault(g.OutputCode), g.TimeGrain)).ToArray(),
                row.Values))
            .ToArray();
        var values = BuildValueColumns(plan, leaves, plan.Shape.ShowGrandTotals);
        var lookup = cells.ToLookup(r => BuildTupleKey(axisCodes, r.Values), StringComparer.Ordinal);
        var output = new List<ReportSheetRowDto>();

        foreach (var axis in axisRows)
        {
            var leaf = Leaf(axis.Values, lookup[BuildTupleKey(axisCodes, axis.Values)]);
            if (plan.RowGroups.Count == 0)
            {
                output.Add(BuildLeafRow(plan, rowColumns, values, leaf, plan.DetailFields.Count > 0));
            }
            else
            {
                var groupRow = BuildGroupRow(plan, rowColumns, values, [leaf], 0, 1, 0, false);
                if (!showGroupValues)
                {
                    groupRow = groupRow with
                    { 
                        Cells = groupRow.Cells.Select((cell, index) => index < rowColumns.Count
                            ? cell
                            : cell with { Value = null, Display = "", Action = null }).ToArray()
                    };
                }
                
                output.Add(groupRow);
                
                if (separateSubtotals)
                    output.Add(BuildSubtotalRow(plan, rowColumns, values, [leaf], 0, 1, 0));
            }
        }
        if (grand is not null && axisRows.Count > 0 && (plan.RowGroups.Count > 0 || plan.DetailFields.Count > 0))
            output.Add(BuildGrandTotalRow(plan, rowColumns, values, [Leaf(grand.Values, grandCells)]));
        
        return new(
            rowColumns.Concat(values.Select(v => v.Column)).ToArray(),
            _headerBuilder.Build(rowColumns, plan.ColumnGroups, leaves, plan.Measures, plan.Shape.ShowGrandTotals),
            output,
            new Dictionary<string, string> { ["sheetBuilder"] = "pivot-page" });

        PivotLeafRow Leaf(IReadOnlyDictionary<string, object?> totals, IEnumerable<ReportDataRow> source)
        {
            var leaf = new PivotLeafRow(
                BuildTupleKey(axisCodes, totals),
                totals,
                plan.RowGroups.Select(g => totals.GetValueOrDefault(g.OutputCode)).ToArray(),
                totals);

            foreach (var cell in source)
            {
                foreach (var measure in plan.Measures)
                {
                    leaf.AddValue(BuildTupleKey(columnCodes, cell.Values), measure, cell.Values.GetValueOrDefault(measure.OutputCode));
                }
            }

            leaf.SetTotals(totals);

            return leaf;
        }
    }
}
