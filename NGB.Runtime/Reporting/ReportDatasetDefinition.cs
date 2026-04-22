using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.Runtime.Reporting;

public sealed class ReportDatasetDefinition
{
    private readonly HashSet<string> _filterableFields;
    private readonly HashSet<string> _groupableFields;
    private readonly HashSet<string> _sortableFields;
    private readonly HashSet<string> _selectableFields;

    public ReportDatasetDefinition(ReportDatasetDto dto)
    {
        if (dto is null)
            throw new NgbConfigurationViolationException("Reporting dataset definition is not configured.");

        Dataset = dto;
        DatasetCodeNorm = CodeNormalizer.NormalizeCodeNorm(dto.DatasetCode, nameof(dto.DatasetCode));

        var fields = new Dictionary<string, ReportDatasetFieldDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in dto.Fields ?? [])
        {
            var runtime = ReportDatasetFieldDefinition.FromDto(field);
            if (!fields.TryAdd(runtime.CodeNorm, runtime))
                throw new NgbConfigurationViolationException($"Dataset '{DatasetCodeNorm}' has duplicate field code '{runtime.CodeNorm}'.");
        }

        var measures = new Dictionary<string, ReportDatasetMeasureDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var measure in dto.Measures ?? [])
        {
            var runtime = ReportDatasetMeasureDefinition.FromDto(measure);
            if (!measures.TryAdd(runtime.CodeNorm, runtime))
                throw new NgbConfigurationViolationException($"Dataset '{DatasetCodeNorm}' has duplicate measure code '{runtime.CodeNorm}'.");
        }

        Fields = fields;
        Measures = measures;
        
        _filterableFields = Fields.Values
            .Where(x => x.Field.IsFilterable)
            .Select(x => x.CodeNorm)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        _groupableFields = Fields.Values
            .Where(x => x.Field.IsGroupable)
            .Select(x => x.CodeNorm)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        _sortableFields = Fields.Values
            .Where(x => x.Field.IsSortable)
            .Select(x => x.CodeNorm)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        _selectableFields = Fields.Values
            .Where(x => x.Field.IsSelectable)
            .Select(x => x.CodeNorm)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string DatasetCodeNorm { get; }
    public ReportDatasetDto Dataset { get; }
    public IReadOnlyDictionary<string, ReportDatasetFieldDefinition> Fields { get; }
    public IReadOnlyDictionary<string, ReportDatasetMeasureDefinition> Measures { get; }

    public bool TryGetField(string fieldCode, out ReportDatasetFieldDefinition field)
        => Fields.TryGetValue(CodeNormalizer.NormalizeCodeNorm(fieldCode, nameof(fieldCode)), out field!);

    public bool TryGetMeasure(string measureCode, out ReportDatasetMeasureDefinition measure)
        => Measures.TryGetValue(CodeNormalizer.NormalizeCodeNorm(measureCode, nameof(measureCode)), out measure!);

    public bool IsFilterableField(string fieldCode)
        => _filterableFields.Contains(CodeNormalizer.NormalizeCodeNorm(fieldCode, nameof(fieldCode)));

    public bool IsGroupableField(string fieldCode)
        => _groupableFields.Contains(CodeNormalizer.NormalizeCodeNorm(fieldCode, nameof(fieldCode)));

    public bool IsSortableField(string fieldCode)
        => _sortableFields.Contains(CodeNormalizer.NormalizeCodeNorm(fieldCode, nameof(fieldCode)));

    public bool IsSelectableField(string fieldCode)
        => _selectableFields.Contains(CodeNormalizer.NormalizeCodeNorm(fieldCode, nameof(fieldCode)));

    public bool SupportsTimeGrain(string fieldCode, ReportTimeGrain? timeGrain)
    {
        if (!TryGetField(fieldCode, out var field))
            return false;

        return field.SupportsTimeGrain(timeGrain);
    }

    public bool SupportsAggregation(string measureCode, ReportAggregationKind aggregation)
    {
        if (!TryGetMeasure(measureCode, out var measure))
            return false;

        var resolvedAggregation = measure.ResolveAggregation(aggregation);
        return measure.SupportsAggregation(resolvedAggregation);
    }

    public ReportAggregationKind ResolveAggregation(string measureCode, ReportAggregationKind aggregation)
    {
        if (!TryGetMeasure(measureCode, out var measure))
            throw new NgbConfigurationViolationException(
                $"Dataset '{DatasetCodeNorm}' does not define measure '{CodeNormalizer.NormalizeCodeNorm(measureCode, nameof(measureCode))}'.");

        return measure.ResolveAggregation(aggregation);
    }
}
