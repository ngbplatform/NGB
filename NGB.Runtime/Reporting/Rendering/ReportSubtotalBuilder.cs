using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Planning;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Rendering;

internal sealed class ReportSubtotalBuilder(ReportCellFormatter cellFormatter)
{
    private readonly ReportCellFormatter _cellFormatter = cellFormatter
        ?? throw new NgbArgumentRequiredException(nameof(cellFormatter));

    public ReportSubtotalAccumulator CreateAccumulator(IReadOnlyList<ReportPlanMeasure> measures) => new(measures);

    public void Add(ReportSubtotalAccumulator accumulator, IReadOnlyDictionary<string, object?> values)
    {
        if (accumulator is null)
            throw new NgbArgumentRequiredException(nameof(accumulator));

        if (values is null)
            throw new NgbArgumentRequiredException(nameof(values));

        accumulator.Add(values);
    }

    public ReportSheetRowDto BuildSummaryRow(
        IReadOnlyList<ReportSheetColumnDto> columns,
        ReportSubtotalAccumulator accumulator,
        string label,
        ReportRowKind rowKind,
        int outlineLevel,
        string? groupKey,
        string semanticRole)
    {
        var cells = new List<ReportCellDto>(columns.Count);
        var labelIndex = columns
            .Select((col, idx) => new { col, idx })
            .FirstOrDefault(x => !string.Equals(x.col.SemanticRole, "measure", StringComparison.OrdinalIgnoreCase))
            ?.idx ?? 0;

        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            if (index == labelIndex)
            {
                cells.Add(_cellFormatter.BuildLabelCell(label, styleKey: semanticRole, semanticRole: semanticRole));
                continue;
            }

            if (accumulator.TryGetValue(column.Code, out var value))
            {
                cells.Add(_cellFormatter.BuildCell(value, column, styleKey: semanticRole, semanticRole: semanticRole));
                continue;
            }

            cells.Add(_cellFormatter.BuildBlankCell(column, styleKey: semanticRole, semanticRole: semanticRole));
        }

        return new ReportSheetRowDto(
            RowKind: rowKind,
            Cells: cells,
            OutlineLevel: outlineLevel,
            GroupKey: groupKey,
            SemanticRole: semanticRole);
    }
}

internal sealed class ReportSubtotalAccumulator
{
    private readonly Dictionary<string, object?> _values;
    private readonly IReadOnlyList<ReportPlanMeasure> _measures;

    public ReportSubtotalAccumulator(IReadOnlyList<ReportPlanMeasure> measures)
    {
        _measures = measures ?? throw new NgbArgumentRequiredException(nameof(measures));
        _values = measures.ToDictionary(x => x.OutputCode, object? (_) => null, StringComparer.OrdinalIgnoreCase);
    }

    public void Add(IReadOnlyDictionary<string, object?> values)
    {
        if (values is null)
            throw new NgbArgumentRequiredException(nameof(values));

        foreach (var measure in _measures)
        {
            var raw = values.GetValueOrDefault(measure.OutputCode);
            if (raw is null)
                continue;

            _values[measure.OutputCode] = measure.DataType switch
            {
                "int64" => ConvertToInt64(_values[measure.OutputCode]) + ConvertToInt64(raw),
                _ => ConvertToDecimal(_values[measure.OutputCode]) + ConvertToDecimal(raw)
            };
        }
    }

    public bool TryGetValue(string columnCode, out object? value) => _values.TryGetValue(columnCode, out value);

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
