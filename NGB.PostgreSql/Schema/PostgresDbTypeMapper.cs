using NGB.Metadata.Base;
using NGB.Persistence.Schema;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Schema;

public sealed class PostgresDbTypeMapper : IDbTypeMapper
{
    public string Provider => "PostgreSQL";

    public string GetExpectedDbType(ColumnType logicalType) => logicalType switch
    {
        ColumnType.String => "text",
        ColumnType.Int32 => "integer",
        ColumnType.Int64 => "bigint",
        ColumnType.Decimal => "numeric",
        ColumnType.Boolean => "boolean",
        ColumnType.Guid => "uuid",
        ColumnType.DateTimeUtc => "timestamp with time zone",
        ColumnType.Date => "date",
        ColumnType.Json => "jsonb",
        _ => throw new NgbInvariantViolationException($"Unsupported ColumnType '{logicalType}'.", new Dictionary<string, object?> { ["logicalType"] = logicalType.ToString() })
    };

    public bool IsCompatible(ColumnType logicalType, string actualDbType)
    {
        var expected = GetExpectedDbType(logicalType);

        // information_schema.data_type may already match the canonical string
        if (actualDbType.Equals(expected, StringComparison.OrdinalIgnoreCase))
            return true;

        // Common aliases from pg_catalog / information_schema
        //
        // NOTE: It is common to store enums as SMALLINT in PostgreSQL.
        // We treat SMALLINT/INT2 as compatible with the logical Int32 to keep schema validation practical.
        return expected.ToLowerInvariant() switch
        {
            "integer" => actualDbType.Equals("int4", StringComparison.OrdinalIgnoreCase)
                         || actualDbType.Equals("int2", StringComparison.OrdinalIgnoreCase)
                         || actualDbType.Equals("smallint", StringComparison.OrdinalIgnoreCase),
            "bigint" => actualDbType.Equals("int8", StringComparison.OrdinalIgnoreCase),
            "numeric" => actualDbType.StartsWith("numeric", StringComparison.OrdinalIgnoreCase)
                         || actualDbType.StartsWith("decimal", StringComparison.OrdinalIgnoreCase),
            "timestamp with time zone" => actualDbType.Equals("timestamptz", StringComparison.OrdinalIgnoreCase),
            "jsonb" => actualDbType.Equals("jsonb", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
