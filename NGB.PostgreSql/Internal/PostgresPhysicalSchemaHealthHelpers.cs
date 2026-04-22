using Dapper;
using NGB.Metadata.Schema;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Internal;

/// <summary>
/// Shared helpers for physical schema health readers (dynamic per-register tables).
/// Keeps the low-level snapshot diffing and append-only guard probing in one place.
/// </summary>
internal static class PostgresPhysicalSchemaHealthHelpers
{
    public static IReadOnlyList<string> GetMissingColumns(
        DbSchemaSnapshot snapshot,
        string tableName,
        IReadOnlyList<string> required)
    {
        if (!snapshot.ColumnsByTable.TryGetValue(tableName, out var cols))
            return required.ToArray();

        var set = cols.Select(c => c.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var c in required)
        {
            if (!set.Contains(c))
                missing.Add(c);
        }

        return missing;
    }

    public static IReadOnlyList<string> GetMissingIndexes(
        DbSchemaSnapshot snapshot,
        string tableName,
        (string[] Columns, bool UniqueRequired, string Label)[] required)
    {
        if (!snapshot.IndexesByTable.TryGetValue(tableName, out var idx))
            return required.Select(r => r.Label).ToArray();

        static bool Matches(DbIndexSchema i, string[] cols, bool uniqueRequired)
        {
            if (uniqueRequired && !i.IsUnique)
                return false;

            if (i.ColumnNames.Count != cols.Length)
                return false;

            for (var j = 0; j < cols.Length; j++)
            {
                if (!string.Equals(i.ColumnNames[j], cols[j], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        var missing = new List<string>();

        foreach (var r in required)
        {
            if (!idx.Any(i => Matches(i, r.Columns, r.UniqueRequired)))
                missing.Add(r.Label);
        }

        return missing;
    }

    public static (bool Exists, IReadOnlyList<string> MissingColumns, IReadOnlyList<string> MissingIndexes) ComputeTableDiff(
        DbSchemaSnapshot snapshot,
        string tableName,
        IReadOnlyList<string> requiredColumns,
        (string[] Columns, bool UniqueRequired, string Label)[] requiredIndexes)
    {
        var exists = snapshot.Tables.Contains(tableName);

        var missingColumns = exists
            ? GetMissingColumns(snapshot, tableName, requiredColumns)
            : requiredColumns.ToArray();

        var missingIndexes = exists
            ? GetMissingIndexes(snapshot, tableName, requiredIndexes)
            : requiredIndexes.Select(r => r.Label).ToArray();

        return (exists, missingColumns, missingIndexes);
    }

    public static async Task<IReadOnlyDictionary<string, bool>> LoadAppendOnlyGuardPresenceAsync(
        IUnitOfWork uow,
        string[] tables,
        CancellationToken ct)
    {
        if (tables.Length == 0)
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        var rows = (await uow.Connection.QueryAsync<AppendOnlyRow>(
            new CommandDefinition(
                """
                SELECT
                    c.relname AS "TableName",
                    BOOL_OR(p.proname = 'ngb_forbid_mutation_of_append_only_table') AS "HasGuard"
                FROM pg_trigger t
                JOIN pg_class c ON c.oid = t.tgrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_proc p ON p.oid = t.tgfoid
                WHERE n.nspname = 'public'
                  AND NOT t.tgisinternal
                  AND c.relname = ANY(@TableNames)
                GROUP BY c.relname;
                """,
                new { TableNames = tables },
                transaction: uow.Transaction,
                cancellationToken: ct))).AsList();

        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in tables)
            map[t] = false;

        foreach (var r in rows)
            map[r.TableName] = r.HasGuard;

        return map;
    }

    private sealed class AppendOnlyRow
    {
        public string TableName { get; init; } = null!;
        public bool HasGuard { get; init; }
    }
}
