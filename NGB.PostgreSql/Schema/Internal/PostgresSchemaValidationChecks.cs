using Dapper;
using NGB.Metadata.Schema;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Schema.Internal;

/// <summary>
/// Shared helper methods for PostgreSQL core schema validation services.
///
/// These checks are intentionally strict: table/index/constraint names are treated as part of the contract
/// because platform migrations are idempotent and rely on those stable names for drift detection.
/// </summary>
internal static class PostgresSchemaValidationChecks
{
    public static void RequireTable(DbSchemaSnapshot snapshot, string tableName, List<string> errors)
    {
        if (!snapshot.Tables.Contains(tableName))
            errors.Add($"Missing table '{tableName}'.");
    }

    public static void RequireColumns(
        DbSchemaSnapshot snapshot,
        string tableName,
        string[] required,
        List<string> errors)
    {
        if (!snapshot.ColumnsByTable.TryGetValue(tableName, out var cols))
        {
            errors.Add($"Cannot read columns for table '{tableName}'.");
            return;
        }

        var set = cols.Select(c => c.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var c in required)
        {
            if (!set.Contains(c))
                errors.Add($"Table '{tableName}' is missing column '{c}'.");
        }
    }

    public static void RequireIndex(DbSchemaSnapshot snapshot, string tableName, string indexName, List<string> errors)
    {
        // PostgresSchemaInspector returns indexes only for tables that currently have them.
        // Treat missing dictionary entries as "no indexes" (not as an inspection failure).
        var idx = snapshot.IndexesByTable.TryGetValue(tableName, out var list)
            ? list
            : [];

        var exists = idx.Any(x => x.IndexName.Equals(indexName, StringComparison.OrdinalIgnoreCase));
        if (!exists)
            errors.Add($"Missing index '{indexName}' on table '{tableName}'.");
    }

    public static void RequireForeignKey(
        DbSchemaSnapshot snapshot,
        string tableName,
        string columnName,
        string referencedTable,
        string referencedColumn,
        List<string> errors)
    {
        // PostgresSchemaInspector returns foreign keys only for tables that currently have them.
        // Treat missing dictionary entries as "no foreign keys" (not as an inspection failure).
        var fks = snapshot.ForeignKeysByTable.TryGetValue(tableName, out var list)
            ? list
            : [];

        var exists = fks.Any(f =>
            f.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase) &&
            f.ReferencedTableName.Equals(referencedTable, StringComparison.OrdinalIgnoreCase) &&
            f.ReferencedColumnName.Equals(referencedColumn, StringComparison.OrdinalIgnoreCase));

        if (!exists)
            errors.Add($"Missing foreign key: {tableName}.{columnName} -> {referencedTable}.{referencedColumn}.");
    }

    public static async Task RequireFunctionAsync(
        IUnitOfWork uow,
        string functionName,
        List<string> errors,
        CancellationToken ct)
    {
        var exists = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM pg_proc p
                JOIN pg_namespace n ON n.oid = p.pronamespace
                WHERE p.proname = @name
                  AND n.nspname = 'public';
                """,
                new { name = functionName },
                transaction: uow.Transaction,
                cancellationToken: ct));

        if (exists == 0)
            errors.Add($"Missing function '{functionName}'.");
    }

    public static async Task RequireTriggerAsync(
        IUnitOfWork uow,
        string triggerName,
        string tableName,
        List<string> errors,
        CancellationToken ct)
    {
        var exists = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM pg_trigger t
                JOIN pg_class cl ON cl.oid = t.tgrelid
                JOIN pg_namespace ns ON ns.oid = cl.relnamespace
                WHERE t.tgname = @trigger
                  AND NOT t.tgisinternal
                  AND ns.nspname = 'public'
                  AND cl.relname = @table;
                """,
                new { trigger = triggerName, table = tableName },
                transaction: uow.Transaction,
                cancellationToken: ct));

        if (exists == 0)
            errors.Add($"Missing trigger '{triggerName}' on '{tableName}'.");
    }

    public static async Task RequireConstraintAsync(
        IUnitOfWork uow,
        string constraintName,
        string tableName,
        List<string> errors,
        CancellationToken ct)
    {
        var exists = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = 'public'
                  AND t.relname = @table
                  AND c.conname = @name;
                """,
                new { table = tableName, name = constraintName },
                transaction: uow.Transaction,
                cancellationToken: ct));

        if (exists == 0)
            errors.Add($"Missing constraint '{constraintName}' on '{tableName}'.");
    }
}
