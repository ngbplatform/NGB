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
public sealed class PostgresSchemaInspector(IUnitOfWork uow) : IDbSchemaInspector
{
    public async Task<DbSchemaSnapshot> GetSnapshotAsync(CancellationToken ct = default)
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

        var tables = (await uow.Connection.QueryAsync<string>(
            new CommandDefinition(tablesSql, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var cols = (await uow.Connection.QueryAsync<DbColumnSchema>(
            new CommandDefinition(colsSql, cancellationToken: ct))).ToList();

        var fks = (await uow.Connection.QueryAsync<DbForeignKeySchema>(
            new CommandDefinition(fksSql, cancellationToken: ct))).ToList();

        var idxRaw = await uow.Connection.QueryAsync<dynamic>(
            new CommandDefinition(idxSql, cancellationToken: ct));

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
            IndexesByTable: idxByTable);
    }
}
