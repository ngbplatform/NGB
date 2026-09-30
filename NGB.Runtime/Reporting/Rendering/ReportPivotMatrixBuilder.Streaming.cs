using System.Runtime.CompilerServices;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Streaming;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Rendering;

internal sealed partial class ReportPivotMatrixBuilder
{
    public async IAsyncEnumerable<ReportRowWrite> BuildStreamingAsync(
        ReportDefinitionRuntimeModel definition,
        ReportQueryPlan plan,
        Func<ReportQueryPlan, CancellationToken, IAsyncEnumerable<ReportStreamingDataRow>> read,
        Action<ReportSheetDto> setTemplate,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var rowColumns = BuildRowAxisColumns(plan);
        var leaves = new List<PivotColumnLeaf>();
        var columnCodes = plan.ColumnGroups.Select(g => g.OutputCode).ToArray();
        var columnFields = plan.ColumnGroups.Select(g => g.FieldCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columnPlan = ReportSummaryStreams.AtGrain(plan, 0, columns: true) with
        {
            Sorts = plan.Sorts
                .Where(s => s.AppliesToColumnAxis || s.GroupKey is null && columnFields.Contains(s.FieldCode))
                .ToArray()
        };
        
        await foreach (var row in read(columnPlan, ct))
        {
            leaves.Add(new(
                BuildTupleKey(columnCodes, row.RawValues),
                columnCodes.Select(c => row.Values.GetValueOrDefault(c)).ToArray(),
                plan.ColumnGroups
                    .Select(g => _cellFormatter.FormatGroupLabel(row.Values.GetValueOrDefault(g.OutputCode), g.TimeGrain))
                    .ToArray(),
                row.Values));

            if (definition.Capabilities.MaxVisibleColumns is { } max
                && (long)leaves.Count * plan.Measures.Count + rowColumns.Count + (plan.Shape.ShowGrandTotals
                    ? plan.Measures.Count
                    : 0) > max)
            {
                EnsureCaps(
                    definition,
                    plan,
                    (long)leaves.Count * plan.Measures.Count + rowColumns.Count + (plan.Shape.ShowGrandTotals
                        ? plan.Measures.Count
                        : 0),
                    0);
            }
        }

        var values = BuildValueColumns(
            plan,
            leaves,
            plan.Shape.ShowGrandTotals && plan.Measures.Count > 0);

        var headers = _headerBuilder.Build(
            rowColumns,
            plan.ColumnGroups,
            leaves,
            plan.Measures,
            plan.Shape.ShowGrandTotals && plan.Measures.Count > 0);

        setTemplate(new(
            rowColumns.Concat(values.Select(v => v.Column)).ToArray(),
            [],
            new(
                Title: definition.Definition.Name,
                Subtitle: definition.Definition.Description,
                IsPivot: true,
                HasRowOutline: plan.RowGroups.Count > 0, 
                HasColumnGroups: true,
                Diagnostics: new Dictionary<string, string> { ["executor"] = "postgres-streaming", ["sheetBuilder"] = "streaming-pivot" }),
            headers));

        if (leaves.Count == 0)
            yield break;

        var ordinal = 0;
        var hasDetails = plan.DetailFields.Count > 0;
        var groups = new List<PivotLeafRow>();
        var streams = new Dictionary<int, IAsyncEnumerator<PivotLeafRow>>();
        
        await using var totals = new ReportSummaryStreams(plan, read, ct);

        IAsyncEnumerator<ReportStreamingDataRow>? leafTotals = null;

        try
        {
            // The row grain also determines its label and safe drilldown identity,
            // independently of which pivot columns contain observations.
            if (hasDetails || plan.RowGroups.Count == 0)
                leafTotals = read(ReportSummaryStreams.AtGrain(plan, plan.RowGroups.Count, details: true), ct).GetAsyncEnumerator(ct);

            await foreach (var leaf in ReadLeavesAsync(plan, read, ct))
            {
                var different = 0;
                while (different < groups.Count && Equals(
                    groups[different].RawValues.GetValueOrDefault(plan.RowGroups[different].OutputCode),
                    leaf.RawValues.GetValueOrDefault(plan.RowGroups[different].OutputCode)))
                {
                    different++;
                }

                for (var level = groups.Count - 1; level >= different; level--)
                {
                    if (ShouldEmitSeparateSubtotal(plan, level))
                        yield return new(ordinal++, BuildSubtotalRow(plan, rowColumns, values, [groups[level]], 0, 1, level));

                    groups.RemoveAt(level);
                }

                for (var level = different; level < plan.RowGroups.Count; level++)
                {
                    if (!streams.TryGetValue(level, out var stream))
                    {
                        stream = ReadLeavesAsync(ReportSummaryStreams.AtGrain(plan, level + 1, columns: true), read, ct).GetAsyncEnumerator(ct);
                        streams.Add(level, stream);
                    }

                    if (!await stream.MoveNextAsync())
                        throw new NgbInvariantViolationException("Pivot summary stream ended early.");

                    var group = stream.Current;
                    for (var i = 0; i <= level; i++)
                    {
                        var code = plan.RowGroups[i].OutputCode;
                        if (!Equals(group.RawValues.GetValueOrDefault(code), leaf.RawValues.GetValueOrDefault(code)))
                            throw new NgbInvariantViolationException("Pivot summary order differs from its detail stream.");
                    }
                    
                    group.SetTotals((await totals.NextAsync(level, leaf.RawValues)).Values);
                    groups.Add(group);

                    yield return new(
                        ordinal++,
                        BuildGroupRow(plan, rowColumns, values, [group], 0, 1, level, hasDetails));
                }

                if (leafTotals is not null)
                {
                    if (!await leafTotals.MoveNextAsync())
                        throw new NgbInvariantViolationException("Pivot leaf totals ended early.");

                    foreach (var code in ReportRowHierarchy.BuildValueCodes(plan))
                    {

                        if (!Equals(leaf.RawValues.GetValueOrDefault(code), leafTotals.Current.RawValues.GetValueOrDefault(code)))
                            throw new NgbInvariantViolationException("Pivot leaf total order differs from its detail stream.");
                    }

                    leaf.SetTotals(leafTotals.Current.Values);
                }

                if (hasDetails || plan.RowGroups.Count == 0)
                    yield return new(ordinal++, BuildLeafRow(plan, rowColumns, values, leaf, hasDetails));
            }

            for (var level = groups.Count - 1; level >= 0; level--)
            {
                if (ShouldEmitSeparateSubtotal(plan, level))
                    yield return new(ordinal++, BuildSubtotalRow(plan, rowColumns, values, [groups[level]], 0, 1, level));
            }

            if (plan.Measures.Count > 0 && plan.Shape.ShowGrandTotals)
            {
                await using var grandStream = ReadLeavesAsync(columnPlan, read, ct).GetAsyncEnumerator(ct);

                if (await grandStream.MoveNextAsync())
                {
                    var grand = grandStream.Current;
                    grand.SetTotals((await totals.NextAsync(-1, new Dictionary<string, object?>())).Values);
                    yield return new(ordinal, BuildGrandTotalRow(plan, rowColumns, values, [grand]));
                }
            }
        }
        finally
        {
            if (leafTotals is not null)
                await leafTotals.DisposeAsync();

            foreach (var stream in streams.Values)
            {
                await stream.DisposeAsync();
            }
        }
    }

    private static async IAsyncEnumerable<PivotLeafRow> ReadLeavesAsync(
        ReportQueryPlan plan,
        Func<ReportQueryPlan, CancellationToken, IAsyncEnumerable<ReportStreamingDataRow>> read,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var axisCodes = ReportRowHierarchy.BuildValueCodes(plan);
        var columnCodes = plan.ColumnGroups.Select(g => g.OutputCode).ToArray();
        PivotLeafRow? leaf = null;
        
        await foreach (var row in read(plan, ct))
        {
            if (leaf is null || axisCodes.Any(code => !Equals(leaf.RawValues.GetValueOrDefault(code), row.RawValues.GetValueOrDefault(code))))
            {
                if (leaf is not null)
                    yield return leaf;

                leaf = new(
                    BuildTupleKey(axisCodes, row.RawValues),
                    axisCodes.ToDictionary(c => c, c => row.Values.GetValueOrDefault(c), StringComparer.OrdinalIgnoreCase),
                    plan.RowGroups.Select(g => row.Values.GetValueOrDefault(g.OutputCode)).ToArray(),
                    row.Values,
                    row.RawValues);
            }

            var columnKey = BuildTupleKey(columnCodes, row.RawValues);

            foreach (var measure in plan.Measures)
            {
                leaf.AddValue(columnKey, measure, row.Values.GetValueOrDefault(measure.OutputCode));
            }
        }

        if (leaf is not null)
            yield return leaf;
    }
}
