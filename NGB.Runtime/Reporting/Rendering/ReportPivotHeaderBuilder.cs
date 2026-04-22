using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Planning;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Rendering;

internal sealed class ReportPivotHeaderBuilder(
    ReportCellFormatter cellFormatter,
    ReportComposableCellActionResolver actionResolver)
{
    private readonly ReportCellFormatter _cellFormatter = cellFormatter
        ?? throw new NgbArgumentRequiredException(nameof(cellFormatter));

    private readonly ReportComposableCellActionResolver _actionResolver = actionResolver
        ?? throw new NgbArgumentRequiredException(nameof(actionResolver));

    public IReadOnlyList<ReportSheetRowDto> Build(
        IReadOnlyList<ReportSheetColumnDto> rowAxisColumns,
        IReadOnlyList<ReportPlanGrouping> columnGroups,
        IReadOnlyList<PivotColumnLeaf> leaves,
        IReadOnlyList<ReportPlanMeasure> measures,
        bool includeTotals)
    {
        if (rowAxisColumns is null)
            throw new NgbArgumentRequiredException(nameof(rowAxisColumns));

        if (columnGroups is null)
            throw new NgbArgumentRequiredException(nameof(columnGroups));

        if (leaves is null)
            throw new NgbArgumentRequiredException(nameof(leaves));

        if (measures is null)
            throw new NgbArgumentRequiredException(nameof(measures));

        if (columnGroups.Count == 0)
            return [];

        var headerDepth = columnGroups.Count + 1;
        var rows = Enumerable.Range(0, headerDepth)
            .Select(_ => new List<ReportCellDto>())
            .ToList();

        foreach (var rowAxisColumn in rowAxisColumns)
        {
            rows[0].Add(_cellFormatter.BuildLabelCell(
                rowAxisColumn.Title,
                styleKey: "header",
                semanticRole: "header",
                rowSpan: headerDepth));
        }

        for (var level = 0; level < columnGroups.Count; level++)
        {
            var row = rows[level];
            var start = 0;
            while (start < leaves.Count)
            {
                var end = start + 1;
                while (end < leaves.Count && HasSamePrefix(leaves[start], leaves[end], level))
                    end++;

                var span = (end - start) * Math.Max(1, measures.Count);
                row.Add(_cellFormatter.BuildLabelCell(
                    leaves[start].Labels[level],
                    styleKey: "header",
                    semanticRole: "header",
                    colSpan: span,
                    action: _actionResolver.ResolveForGroup(columnGroups[level], leaves[start].SourceValues)));
                start = end;
            }

            if (includeTotals && level == 0)
            {
                row.Add(_cellFormatter.BuildLabelCell(
                    "Total",
                    styleKey: "header",
                    semanticRole: "header",
                    colSpan: Math.Max(1, measures.Count),
                    rowSpan: columnGroups.Count));
            }
        }

        var leafRow = rows[^1];
        foreach (var _ in leaves)
        {
            foreach (var measure in measures)
            {
                leafRow.Add(_cellFormatter.BuildLabelCell(
                    measure.Label,
                    styleKey: "header",
                    semanticRole: "header"));
            }
        }

        if (includeTotals)
        {
            foreach (var measure in measures)
            {
                leafRow.Add(_cellFormatter.BuildLabelCell(
                    measure.Label,
                    styleKey: "header",
                    semanticRole: "header"));
            }
        }

        return rows
            .Select((cells, index) => new ReportSheetRowDto(
                RowKind: ReportRowKind.Header,
                Cells: cells,
                OutlineLevel: 0,
                GroupKey: $"pivot-header:{index}",
                SemanticRole: "header"))
            .ToList();
    }

    private static bool HasSamePrefix(PivotColumnLeaf left, PivotColumnLeaf right, int level)
    {
        for (var index = 0; index <= level; index++)
        {
            if (!Equals(left.Values[index], right.Values[index]))
                return false;
        }

        return true;
    }
}
