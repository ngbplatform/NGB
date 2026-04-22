using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.PostgreSql.Reporting;

public sealed class PostgresReportMeasureBinding
{
    public PostgresReportMeasureBinding(string measureCode, string sqlExpression, string dataType)
    {
        if (string.IsNullOrWhiteSpace(measureCode))
            throw new NgbArgumentRequiredException(nameof(measureCode));

        if (string.IsNullOrWhiteSpace(sqlExpression))
            throw new NgbArgumentRequiredException(nameof(sqlExpression));

        if (string.IsNullOrWhiteSpace(dataType))
            throw new NgbArgumentRequiredException(nameof(dataType));

        MeasureCodeNorm = CodeNormalizer.NormalizeCodeNorm(measureCode, nameof(measureCode));
        SqlExpression = sqlExpression;
        DataType = dataType;
    }

    public string MeasureCodeNorm { get; }
    public string SqlExpression { get; }
    public string DataType { get; }

    public string ResolveAggregateExpression(ReportAggregationKind aggregation)
        => aggregation switch
        {
            ReportAggregationKind.Sum => $"SUM({SqlExpression})",
            ReportAggregationKind.Count => $"COUNT({SqlExpression})",
            ReportAggregationKind.Min => $"MIN({SqlExpression})",
            ReportAggregationKind.Max => $"MAX({SqlExpression})",
            ReportAggregationKind.Average => $"AVG({SqlExpression})",
            _ => throw new NgbConfigurationViolationException($"Measure '{MeasureCodeNorm}' does not support aggregation '{aggregation}'.")
        };
}
