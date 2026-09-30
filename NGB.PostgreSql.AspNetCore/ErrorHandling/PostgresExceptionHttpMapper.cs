using NGB.Hosting.AspNetCore.ErrorHandling;
using NGB.Tools.Exceptions;
using Npgsql;

namespace NGB.PostgreSql.AspNetCore.ErrorHandling;

public sealed class PostgresExceptionHttpMapper : INgbExceptionHttpMapper
{
    public NgbExceptionHttpMapping? TryMap(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            PostgresException postgres => MapServerException(postgres),
            NpgsqlException npgsql => MapClientException(npgsql),
            _ => null
        };
    }

    private static NgbExceptionHttpMapping MapServerException(PostgresException exception)
    {
        var (statusCode, errorCode) = exception.SqlState switch
        {
            "23505" => (409, "ngb.conflict.unique_violation"),
            "23503" => (409, "ngb.conflict.foreign_key_violation"),
            "40001" => (409, "ngb.conflict.serialization_failure"),
            "40P01" => (409, "ngb.conflict.deadlock_detected"),
            "53300" => (503, "ngb.db.too_many_connections"),
            "53400" => (503, "ngb.db.configuration_limit_exceeded"),
            "57P03" => (503, "ngb.db.cannot_connect_now"),
            _ => (500, "ngb.db.error")
        };

        var context = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sqlState"] = exception.SqlState
        };

        AddWhenPresent(context, "constraint", exception.ConstraintName);
        AddWhenPresent(context, "table", exception.TableName);
        AddWhenPresent(context, "column", exception.ColumnName);

        return new NgbExceptionHttpMapping(
            statusCode,
            errorCode,
            statusCode >= 500 ? NgbErrorKind.Infrastructure : NgbErrorKind.Conflict,
            context);
    }

    private static NgbExceptionHttpMapping MapClientException(NpgsqlException exception)
    {
        var errorCode = HasTimeoutCause(exception)
            ? "ngb.db.connection_pool_exhausted"
            : "ngb.db.unavailable";

        return new NgbExceptionHttpMapping(503, errorCode, NgbErrorKind.Infrastructure);
    }

    private static bool HasTimeoutCause(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is TimeoutException)
                return true;

            if (current.Message.Contains("pool", StringComparison.OrdinalIgnoreCase)
                && current.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (current.InnerException is null)
                break;
        }

        return false;
    }

    private static void AddWhenPresent(IDictionary<string, object?> context, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            context[key] = value;
    }
}
