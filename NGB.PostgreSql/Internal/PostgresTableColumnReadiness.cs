using Dapper;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Internal;

/// <summary>
/// Cheap, read-only physical-shape check used on dynamic-table write paths.
/// DDL repair is invoked only when the table or a required metadata-driven column is absent.
/// </summary>
internal static class PostgresTableColumnReadiness
{
    public static async Task<bool> HasRequiredColumnsAsync(
        IUnitOfWork uow,
        string tableName,
        IReadOnlyCollection<string> requiredColumns,
        CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
            SELECT
                to_regclass(@TableName) IS NOT NULL
                AND NOT EXISTS (
                    SELECT 1
                    FROM unnest(@RequiredColumns::text[]) AS required(column_name)
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM pg_attribute a
                        WHERE a.attrelid = to_regclass(@TableName)
                          AND a.attnum > 0
                          AND NOT a.attisdropped
                          AND a.attname = required.column_name
                    )
                );
            """;

        return await uow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            sql,
            new { TableName = tableName, RequiredColumns = requiredColumns.ToArray() },
            transaction: uow.Transaction,
            cancellationToken: ct));
    }
}
