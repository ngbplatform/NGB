using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.PostgreSql.Reporting;

public sealed class PostgresReportDatasetBinding
{
    private readonly Dictionary<string, PostgresReportFieldBinding> _fields;
    private readonly Dictionary<string, PostgresReportMeasureBinding> _measures;

    public PostgresReportDatasetBinding(
        string datasetCode,
        string fromSql,
        IReadOnlyList<PostgresReportFieldBinding> fields,
        IReadOnlyList<PostgresReportMeasureBinding> measures,
        string? baseWhereSql = null)
    {
        if (string.IsNullOrWhiteSpace(datasetCode))
            throw new NgbArgumentRequiredException(nameof(datasetCode));

        if (string.IsNullOrWhiteSpace(fromSql))
            throw new NgbArgumentRequiredException(nameof(fromSql));

        DatasetCodeNorm = CodeNormalizer.NormalizeCodeNorm(datasetCode, nameof(datasetCode));
        FromSql = fromSql;
        BaseWhereSql = baseWhereSql;

        _fields = new Dictionary<string, PostgresReportFieldBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields ?? [])
        {
            if (!_fields.TryAdd(field.FieldCodeNorm, field))
                throw new NgbConfigurationViolationException($"PostgreSQL reporting dataset '{DatasetCodeNorm}' has duplicate field binding '{field.FieldCodeNorm}'.");
        }

        _measures = new Dictionary<string, PostgresReportMeasureBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var measure in measures ?? [])
        {
            if (!_measures.TryAdd(measure.MeasureCodeNorm, measure))
                throw new NgbConfigurationViolationException($"PostgreSQL reporting dataset '{DatasetCodeNorm}' has duplicate measure binding '{measure.MeasureCodeNorm}'.");
        }
    }

    public string DatasetCodeNorm { get; }
    public string FromSql { get; }
    public string? BaseWhereSql { get; }
    public IReadOnlyDictionary<string, PostgresReportFieldBinding> Fields => _fields;
    public IReadOnlyDictionary<string, PostgresReportMeasureBinding> Measures => _measures;

    public PostgresReportFieldBinding GetField(string fieldCode)
    {
        var codeNorm = CodeNormalizer.NormalizeCodeNorm(fieldCode, nameof(fieldCode));
        if (_fields.TryGetValue(codeNorm, out var binding))
            return binding;

        throw new NgbConfigurationViolationException($"PostgreSQL reporting dataset '{DatasetCodeNorm}' does not define field binding '{codeNorm}'.");
    }

    public PostgresReportMeasureBinding GetMeasure(string measureCode)
    {
        var codeNorm = CodeNormalizer.NormalizeCodeNorm(measureCode, nameof(measureCode));
        if (_measures.TryGetValue(codeNorm, out var binding))
            return binding;

        throw new NgbConfigurationViolationException($"PostgreSQL reporting dataset '{DatasetCodeNorm}' does not define measure binding '{codeNorm}'.");
    }
}
