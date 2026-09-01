using Dapper;
using NGB.Metadata.Schema;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Schema;

/// <summary>
/// PostgreSQL schema inspector for metadata schema validators.
/// Loads the schema in bulk (few round-trips) and returns an in-memory snapshot.
/// Requires an open connection (transaction is optional).
/// </summary>
public sealed class PostgresSchemaInspector(IUnitOfWork uow) : IDbSchemaInspector, IDbSchemaSnapshotScopeFactory
{
    private readonly SemaphoreSlim _snapshotScopeGate = new(1, 1);
    private DbSchemaSnapshot? _scopedSnapshot;

    public Task<DbSchemaSnapshot> GetSnapshotAsync(CancellationToken ct = default)
        => Volatile.Read(ref _scopedSnapshot) is { } snapshot
            ? Task.FromResult(snapshot)
            : LoadSnapshotAsync(ct);

    public async ValueTask<IAsyncDisposable> BeginSnapshotScopeAsync(CancellationToken ct = default)
    {
        await _snapshotScopeGate.WaitAsync(ct);
        try
        {
            _scopedSnapshot = await LoadSnapshotAsync(ct);
            return new SnapshotScope(this);
        }
        catch
        {
            _snapshotScopeGate.Release();
            throw;
        }
    }

    private async Task<DbSchemaSnapshot> LoadSnapshotAsync(CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        
        const string tablesSql = """
                                 SELECT table_name
                                 FROM information_schema.tables
                                 WHERE table_schema = 'public' AND table_type = 'BASE TABLE';
                                 """;

        const string colsSql = """
                               SELECT table_name AS TableName,
                                      column_name AS ColumnName,
                                      data_type AS DbType,
                                      (is_nullable = 'YES') AS IsNullable,
                                      character_maximum_length AS CharacterMaximumLength
                               FROM information_schema.columns
                               WHERE table_schema = 'public';
                               """;

        const string fksSql = """
                              SELECT
                                  tc.table_name AS TableName,
                                  tc.constraint_name AS ConstraintName,
                                  kcu.column_name AS ColumnName,
                                  ccu.table_name AS ReferencedTableName,
                                  ccu.column_name AS ReferencedColumnName
                              FROM information_schema.table_constraints tc
                              JOIN information_schema.key_column_usage kcu
                                   ON tc.constraint_name = kcu.constraint_name
                                  AND tc.table_schema = kcu.table_schema
                              JOIN information_schema.constraint_column_usage ccu
                                   ON ccu.constraint_name = tc.constraint_name
                                  AND ccu.table_schema = tc.table_schema
                              WHERE tc.table_schema = 'public'
                                AND tc.constraint_type = 'FOREIGN KEY';
                              """;

        // indexes (exclude primary keys) via pg_catalog for accuracy
        const string idxSql = """
                              SELECT
                                  t.relname AS TableName,
                                  i.relname AS IndexName,
                                  ix.indisunique AS IsUnique,
                                  array_agg(a.attname ORDER BY x.ord) AS ColumnNames
                              FROM pg_class t
                              JOIN pg_index ix ON t.oid = ix.indrelid
                              JOIN pg_class i ON i.oid = ix.indexrelid
                              JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS x(attnum, ord) ON TRUE
                              JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = x.attnum
                              JOIN pg_namespace n ON n.oid = t.relnamespace
                              WHERE n.nspname = 'public'
                                AND t.relkind = 'r'
                                AND NOT ix.indisprimary
                              GROUP BY t.relname, i.relname, ix.indisunique;
                              """;

        const string functionsSql = """
                                    SELECT DISTINCT p.proname
                                    FROM pg_proc p
                                    JOIN pg_namespace n ON n.oid = p.pronamespace
                                    WHERE n.nspname = 'public';
                                    """;

        const string triggersSql = """
                                   SELECT t.tgname AS "TriggerName",
                                          cl.relname AS "TableName"
                                   FROM pg_trigger t
                                   JOIN pg_class cl ON cl.oid = t.tgrelid
                                   JOIN pg_namespace ns ON ns.oid = cl.relnamespace
                                   WHERE ns.nspname = 'public'
                                     AND NOT t.tgisinternal;
                                   """;

        const string constraintsSql = """
                                      SELECT c.conname AS "ConstraintName",
                                             t.relname AS "TableName"
                                      FROM pg_constraint c
                                      JOIN pg_class t ON t.oid = c.conrelid
                                      JOIN pg_namespace n ON n.oid = t.relnamespace
                                      WHERE n.nspname = 'public';
                                      """;

        // All catalog reads describe one logical snapshot. Sending them as one command avoids
        // four network round-trips and keeps the inspector cheap enough for startup/health paths.
        var snapshotSql = $"{tablesSql}\n{colsSql}\n{fksSql}\n{idxSql}\n{functionsSql}\n{triggersSql}\n{constraintsSql}";
        await using var grid = await uow.Connection.QueryMultipleAsync(
            new CommandDefinition(
                snapshotSql,
                transaction: uow.Transaction,
                cancellationToken: ct));

        var tables = (await grid.ReadAsync<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cols = (await grid.ReadAsync<DbColumnSchema>()).ToList();
        var fks = (await grid.ReadAsync<DbForeignKeySchema>()).ToList();
        var idxRaw = await grid.ReadAsync<dynamic>();
        var functions = (await grid.ReadAsync<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var triggers = (await grid.ReadAsync<DbTriggerSchema>()).ToList();
        var constraints = (await grid.ReadAsync<DbConstraintSchema>()).ToList();

        var idx = new List<DbIndexSchema>();
        foreach (var row in idxRaw)
        {
            string tableName = row.tablename;
            string indexName = row.indexname;
            bool isUnique = row.isunique;
            var colNames = (string[])row.columnnames;
            idx.Add(new DbIndexSchema(tableName, indexName, colNames, isUnique));
        }

        var colsByTable = cols
            .GroupBy(c => c.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, IReadOnlyList<DbColumnSchema> (g) => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var fksByTable = fks
            .GroupBy(f => f.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, IReadOnlyList<DbForeignKeySchema> (g) => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var idxByTable = idx
            .GroupBy(i => i.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, IReadOnlyList<DbIndexSchema> (g) => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return new DbSchemaSnapshot(
            Tables: tables,
            ColumnsByTable: colsByTable,
            ForeignKeysByTable: fksByTable,
            IndexesByTable: idxByTable)
        {
            DatabaseObjects = new DbSchemaObjectSnapshot(functions, triggers, constraints)
        };
    }

    private void EndSnapshotScope()
    {
        Volatile.Write(ref _scopedSnapshot, null);
        _snapshotScopeGate.Release();
    }

    private sealed class SnapshotScope(PostgresSchemaInspector owner) : IAsyncDisposable
    {
        private PostgresSchemaInspector? _currentOwner = owner;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _currentOwner, null)?.EndSnapshotScope();
            return ValueTask.CompletedTask;
        }
    }
}
