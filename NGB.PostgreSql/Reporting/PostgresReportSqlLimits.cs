using System.Text;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Reporting;

/// <summary>Final provider guards, including predicates outside a row-key selection.</summary>
internal static class PostgresReportSqlLimits
{
    // PostgreSQL's Bind protocol supports at most 65,535 query parameters.
    // https://www.postgresql.org/docs/current/limits.html
    public const int MaxBindParameters = ushort.MaxValue;

    // Application budget, not a PostgreSQL limit. Report SQL is a generated
    // template with parameterized values; cap its UTF-8 text at one MiB.
    public const int MaxStatementTextBytes = 1024 * 1024;

    public static void Validate(string sql, int parameterCount)
    {
        if (parameterCount > MaxBindParameters)
            throw new NgbArgumentInvalidException("report", $"Report query exceeds {MaxBindParameters} SQL parameters.");

        if (Encoding.UTF8.GetByteCount(sql) > MaxStatementTextBytes)
            throw new NgbArgumentInvalidException("report", $"Report query exceeds {MaxStatementTextBytes} bytes of SQL text. Simplify its layout or dataset expressions.");
    }
}
