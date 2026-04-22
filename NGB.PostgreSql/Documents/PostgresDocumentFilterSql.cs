using System.Data;
using System.Globalization;
using Dapper;
using NGB.Metadata.Base;
using NGB.Persistence.Documents.Universal;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Documents;

public static class PostgresDocumentFilterSql
{
    public static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new NgbArgumentInvalidException(nameof(identifier), "Identifier is required.");

        return '"' + identifier.Replace("\"", "\"\"") + '"';
    }

    public static string Qualify(string alias, string identifier) => $"{alias}.{QuoteIdentifier(identifier)}";

    public static string BuildPredicate(
        string sqlExpression,
        DocumentFilter filter,
        string parameterName,
        DynamicParameters parameters)
    {
        var values = NormalizeValues(filter);

        switch (filter.ValueType)
        {
            case ColumnType.Guid:
                return AddSingleOrMany(
                    sqlExpression,
                    parameterName,
                    values.Select(value =>
                    {
                        if (!Guid.TryParse(value, out var parsed))
                            throw new NgbArgumentInvalidException(filter.Key, $"'{filter.Key}' must be a valid guid.");

                        return parsed;
                    }).ToArray(),
                    parameters,
                    DbType.Guid);

            case ColumnType.Int32:
                return AddSingleOrMany(
                    sqlExpression,
                    parameterName,
                    values.Select(value =>
                    {
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                            throw new NgbArgumentInvalidException(filter.Key, $"'{filter.Key}' must be a valid integer.");

                        return parsed;
                    }).ToArray(),
                    parameters,
                    DbType.Int32);

            case ColumnType.Int64:
                return AddSingleOrMany(
                    sqlExpression,
                    parameterName,
                    values.Select(value =>
                    {
                        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                            throw new NgbArgumentInvalidException(filter.Key, $"'{filter.Key}' must be a valid integer.");

                        return parsed;
                    }).ToArray(),
                    parameters,
                    DbType.Int64);

            case ColumnType.Decimal:
                return AddSingleOrMany(
                    sqlExpression,
                    parameterName,
                    values.Select(value =>
                    {
                        if (!TryParseDecimal(value, out var parsed))
                            throw new NgbArgumentInvalidException(filter.Key, $"'{filter.Key}' must be a valid decimal.");

                        return parsed;
                    }).ToArray(),
                    parameters,
                    DbType.Decimal);

            case ColumnType.Boolean:
                return AddSingleOrMany(
                    sqlExpression,
                    parameterName,
                    values.Select(value =>
                    {
                        if (!TryParseBoolean(value, out var parsed))
                            throw new NgbArgumentInvalidException(filter.Key, $"'{filter.Key}' must be true or false.");

                        return parsed;
                    }).ToArray(),
                    parameters,
                    DbType.Boolean);

            case ColumnType.Date:
                return AddSingleOrMany(
                    $"{sqlExpression}::date",
                    parameterName,
                    values.Select(value =>
                    {
                        if (!DateOnly.TryParse(value, out var parsed))
                            throw new NgbArgumentInvalidException(filter.Key, $"'{filter.Key}' must be a valid date.");

                        return parsed;
                    }).ToArray(),
                    parameters,
                    DbType.Date);

            case ColumnType.DateTimeUtc:
                return AddSingleOrMany(
                    sqlExpression,
                    parameterName,
                    values.Select(value =>
                    {
                        if (!DateTime.TryParse(
                                value,
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                out var parsed))
                        {
                            throw new NgbArgumentInvalidException(filter.Key, $"'{filter.Key}' must be a valid UTC date/time.");
                        }

                        return parsed;
                    }).ToArray(),
                    parameters,
                    DbType.DateTime2);

            case ColumnType.String:
            default:
                return AddSingleOrMany(sqlExpression, parameterName, values, parameters, DbType.String);
        }
    }

    private static string[] NormalizeValues(DocumentFilter filter)
    {
        var values = (filter.Values)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();

        if (values.Length == 0)
            throw new NgbArgumentInvalidException(filter.Key, $"'{filter.Key}' requires at least one value.");

        return values;
    }

    private static string AddSingleOrMany<T>(
        string sqlExpression,
        string parameterName,
        IReadOnlyList<T> values,
        DynamicParameters parameters,
        DbType dbType)
    {
        if (values.Count == 1)
        {
            parameters.Add(parameterName, values[0], dbType: dbType);
            return $"{sqlExpression} = @{parameterName}";
        }

        parameters.Add(parameterName, values.ToArray());
        return $"{sqlExpression} = ANY(@{parameterName})";
    }

    private static bool TryParseDecimal(string value, out decimal parsed)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed)
           || decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed);

    private static bool TryParseBoolean(string value, out bool parsed)
    {
        if (bool.TryParse(value, out parsed))
            return true;

        switch (value.Trim().ToLowerInvariant())
        {
            case "1":
            case "yes":
            case "y":
                parsed = true;
                return true;
            case "0":
            case "no":
            case "n":
                parsed = false;
                return true;
            default:
                parsed = false;
                return false;
        }
    }
}
