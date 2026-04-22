using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.PostgreSql.Reporting;

public sealed class PostgresReportFieldBinding
{
    public PostgresReportFieldBinding(
        string fieldCode,
        string sqlExpression,
        string dataType,
        string? dayBucketSqlExpression = null,
        string? weekBucketSqlExpression = null,
        string? monthBucketSqlExpression = null,
        string? quarterBucketSqlExpression = null,
        string? yearBucketSqlExpression = null)
    {
        if (string.IsNullOrWhiteSpace(fieldCode))
            throw new NgbArgumentRequiredException(nameof(fieldCode));

        if (string.IsNullOrWhiteSpace(sqlExpression))
            throw new NgbArgumentRequiredException(nameof(sqlExpression));

        if (string.IsNullOrWhiteSpace(dataType))
            throw new NgbArgumentRequiredException(nameof(dataType));

        FieldCodeNorm = CodeNormalizer.NormalizeCodeNorm(fieldCode, nameof(fieldCode));
        SqlExpression = sqlExpression;
        DataType = dataType;
        DayBucketSqlExpression = dayBucketSqlExpression;
        WeekBucketSqlExpression = weekBucketSqlExpression;
        MonthBucketSqlExpression = monthBucketSqlExpression;
        QuarterBucketSqlExpression = quarterBucketSqlExpression;
        YearBucketSqlExpression = yearBucketSqlExpression;
    }

    public string FieldCodeNorm { get; }
    public string SqlExpression { get; }
    public string DataType { get; }
    public string? DayBucketSqlExpression { get; }
    public string? WeekBucketSqlExpression { get; }
    public string? MonthBucketSqlExpression { get; }
    public string? QuarterBucketSqlExpression { get; }
    public string? YearBucketSqlExpression { get; }

    public string ResolveExpression(ReportTimeGrain? timeGrain)
        => timeGrain switch
        {
            null => SqlExpression,
            ReportTimeGrain.Day => DayBucketSqlExpression ?? throw new NgbConfigurationViolationException($"Field '{FieldCodeNorm}' does not define a Day bucket SQL expression."),
            ReportTimeGrain.Week => WeekBucketSqlExpression ?? throw new NgbConfigurationViolationException($"Field '{FieldCodeNorm}' does not define a Week bucket SQL expression."),
            ReportTimeGrain.Month => MonthBucketSqlExpression ?? throw new NgbConfigurationViolationException($"Field '{FieldCodeNorm}' does not define a Month bucket SQL expression."),
            ReportTimeGrain.Quarter => QuarterBucketSqlExpression ?? throw new NgbConfigurationViolationException($"Field '{FieldCodeNorm}' does not define a Quarter bucket SQL expression."),
            ReportTimeGrain.Year => YearBucketSqlExpression ?? throw new NgbConfigurationViolationException($"Field '{FieldCodeNorm}' does not define a Year bucket SQL expression."),
            _ => throw new NgbConfigurationViolationException($"Field '{FieldCodeNorm}' does not support time grain '{timeGrain}'.")
        };
}
