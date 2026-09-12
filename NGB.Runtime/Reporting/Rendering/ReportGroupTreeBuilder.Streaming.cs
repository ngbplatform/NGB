using System.Runtime.CompilerServices;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Runs;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Rendering;

internal sealed partial class ReportGroupTreeBuilder
{
    public async IAsyncEnumerable<ReportRowWrite> BuildStreamingAsync(
        ReportQueryPlan plan,
        IReadOnlyList<ReportSheetColumnDto> columns,
        Func<ReportQueryPlan, CancellationToken, IAsyncEnumerable<ReportDataRow>> read,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var ordinal = 0;
        var hasRows = false;

        var details = plan.DetailFields
            .Select(x => x.OutputCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var measures = plan.Measures
            .Select(x => x.OutputCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hasDetailRows = plan.Shape.ShowDetails || plan.DetailFields.Count > 0;
        var open = new List<OpenGroupState>();

        await using var summaries = new ReportSummaryStreams(plan, read, ct);
        ReportDataRow? previous = null;
        await foreach (var data in read(plan, ct))
        {
            hasRows = true;
            if (plan.RowGroups.Count == 0)
            {
                if (plan.Measures.Count > 0 && plan.DetailFields.Count == 0 && plan.Shape.ShowGrandTotals)
                {
                    var total = _subtotalBuilder.CreateAccumulator(plan.Measures);
                    total.Add(data.Values);

                    yield return new(
                        ordinal++,
                        _subtotalBuilder.BuildSummaryRow(
                            columns, total,
                            "Total",
                            ReportRowKind.Total,
                            0,
                            "grand-total",
                            "grand-total"));
                }
                else
                {
                    
                    yield return new(
                        ordinal++,
                        new(
                            ReportRowKind.Detail,
                            columns
                                .Select(col => 
                                    _cellFormatter.BuildCell(data.Values.GetValueOrDefault(col.Code),
                                        col,
                                        semanticRole: "detail",
                                        action: _actionResolver.ResolveForDetailColumn(col.Code, data.Values)))
                                .ToArray(),
                        SemanticRole: "detail"));
                }

                continue;
            }

            var different = previous is null
                ? 0
                : FindFirstDifferentLevel(plan.RowGroups, previous.Values, data.Values);

            for (var level = open.Count - 1; level >= different; level--)
            {
                if (ShouldEmitSeparateSubtotal(plan, level))
                    yield return new(ordinal++, BuildSubtotalRow(columns, open[level]));

                open.RemoveAt(level);
            }
            
            for (var level = different; level < plan.RowGroups.Count; level++)
            {
                var grouping = plan.RowGroups[level];
                var total = _subtotalBuilder.CreateAccumulator(plan.Measures);
                var summary = plan.Measures.Count > 0
                    ? (await summaries.NextAsync(level, data.Values)).Values
                    : data.Values;

                if (plan.Measures.Count > 0)
                    total.Add(summary);

                var state = new OpenGroupState(
                    level, grouping,
                    data.Values.GetValueOrDefault(grouping.OutputCode),
                    System.Text.Json.JsonSerializer.Serialize(plan.RowGroups.Take(level + 1)
                        .Select(g => new
                        {
                            g.OutputCode,
                            Value = data.Values.GetValueOrDefault(g.OutputCode)
                        })),
                    total,
                    ordinal,
                    summary);

                open.Add(state);

                yield return new(ordinal++, BuildGroupRow(columns, plan, state, total, hasDetailRows));
            }

            if (hasDetailRows)
            {
                yield return new(
                    ordinal++,
                    BuildDetailRow(columns, plan, data.Values, open[^1].GroupKey, details, measures));
            }

            previous = data;
        }

        for (var level = open.Count - 1; level >= 0; level--)
        {
            if (ShouldEmitSeparateSubtotal(plan, level))
                yield return new(ordinal++, BuildSubtotalRow(columns, open[level]));
        }

        if (hasRows && plan.Measures.Count > 0 && plan.Shape.ShowGrandTotals && (plan.RowGroups.Count > 0 || plan.DetailFields.Count > 0))
        {
            var total = _subtotalBuilder.CreateAccumulator(plan.Measures);
            total.Add((await summaries.NextAsync(-1, new Dictionary<string, object?>())).Values);

            yield return new(
                ordinal,
                _subtotalBuilder.BuildSummaryRow(columns, total, "Total", ReportRowKind.Total, 0, "grand-total", "grand-total"));
        }
    }
}

/// <summary>Totals are calculated over the original filtered dataset at each requested grain.
/// Averaging averages or summing distinct counts would produce incorrect report totals.</summary>
internal sealed class ReportSummaryStreams(
    ReportQueryPlan plan,
    Func<ReportQueryPlan, CancellationToken, IAsyncEnumerable<ReportDataRow>> read,
    CancellationToken ct)
    : IAsyncDisposable
{
    private readonly Dictionary<int, IAsyncEnumerator<ReportDataRow>> _streams = [];

    public static ReportQueryPlan AtGrain(ReportQueryPlan plan, int levels, bool columns = false, bool details = false)
    {
        var groups = plan.RowGroups.Take(levels).ToArray();
        var fields = groups.Select(g => g.FieldCode)
            .Concat(details
                ? plan.DetailFields.Select(f => f.FieldCode)
                : [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return plan with
        {
            RowGroups = groups,
            DetailFields = details ? plan.DetailFields : [],
            ColumnGroups = columns ? plan.ColumnGroups : [],
            Sorts = plan.Sorts
                .Where(s => !s.AppliesToColumnAxis
                    && (fields.Contains(s.FieldCode) || s.MeasureCode is not null)
                    && (s.GroupKey is null || groups.Any(g => g.GroupKey == s.GroupKey)))
                .ToArray()
        };
    }

    public async Task<ReportDataRow> NextAsync(int level, IReadOnlyDictionary<string, object?> current)
    {
        if (!_streams.TryGetValue(level, out var stream))
        {
            stream = read(AtGrain(plan, level + 1), ct).GetAsyncEnumerator(ct);
            _streams.Add(level, stream);
        }

        if (!await stream.MoveNextAsync())
            throw new NgbInvariantViolationException("Report summary stream ended before its detail stream.");

        for (var i = 0; i <= level; i++)
        {
            var code = plan.RowGroups[i].OutputCode;
            if (!Equals(current.GetValueOrDefault(code), stream.Current.Values.GetValueOrDefault(code)))
                throw new NgbInvariantViolationException("Report summary order does not match the detail hierarchy.");
        }

        return stream.Current;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var stream in _streams.Values)
        {
            await stream.DisposeAsync();
        }
    }
}
