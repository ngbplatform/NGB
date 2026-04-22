using Dapper;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Catalogs;

internal sealed class PostgresCatalogPartsWriter(IUnitOfWork uow) : ICatalogPartsWriter
{
    public async Task ReplacePartsAsync(
        IReadOnlyList<CatalogTableMetadata> partTables,
        Guid catalogId,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> rowsByTable,
        CancellationToken ct = default)
    {
        if (catalogId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(catalogId));

        if (partTables is null)
            throw new NgbArgumentRequiredException(nameof(partTables));

        if (rowsByTable is null)
            throw new NgbArgumentRequiredException(nameof(rowsByTable));

        if (partTables.Count == 0)
            return;

        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        foreach (var t in partTables)
        {
            if (t.Kind != TableKind.Part)
                continue;

            var tableName = t.TableName;
            if (string.IsNullOrWhiteSpace(tableName))
                throw new NgbArgumentInvalidException(nameof(partTables), "Part table name is required.");

            var deleteSql = $"DELETE FROM {Qi(tableName)} WHERE catalog_id = @catalogId;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                deleteSql,
                new { catalogId },
                transaction: uow.Transaction,
                cancellationToken: ct));

            rowsByTable.TryGetValue(tableName, out var rows);
            rows ??= [];

            if (rows.Count == 0)
                continue;

            var allowed = t.Columns
                .Where(c => !IsCatalogId(c.ColumnName) && c.ColumnType != ColumnType.Json)
                .Select(c => c.ColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var usedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                if (r is null)
                    throw new NgbArgumentInvalidException(nameof(rowsByTable), $"Null row is not allowed for '{tableName}'.");

                foreach (var k in r.Keys)
                {
                    if (IsCatalogId(k))
                        throw new NgbArgumentInvalidException(nameof(rowsByTable), $"'catalog_id' must not be provided for '{tableName}'.");

                    if (!allowed.Contains(k))
                        throw new NgbArgumentInvalidException(nameof(rowsByTable), $"Unknown column '{k}' for '{tableName}'.");

                    usedColumns.Add(k);
                }
            }

            var orderedColumns = t.Columns
                .Where(c => usedColumns.Contains(c.ColumnName))
                .Select(c => c.ColumnName)
                .ToList();

            if (orderedColumns.Count == 0)
                throw new NgbArgumentInvalidException(nameof(rowsByTable), $"No insertable columns provided for '{tableName}'.");

            var insertColumnsSql = new List<string> { "catalog_id" };
            insertColumnsSql.AddRange(orderedColumns.Select(Qi));

            var p = new DynamicParameters();
            p.Add("catalogId", catalogId);

            var valuesSql = new List<string>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var rowParams = new List<string> { "@catalogId" };

                foreach (var col in orderedColumns)
                {
                    var paramName = $"p_{col}_{i}";
                    r.TryGetValue(col, out var value);
                    p.Add(paramName, value);
                    rowParams.Add("@" + paramName);
                }

                valuesSql.Add("(" + string.Join(", ", rowParams) + ")");
            }

            var insertSql = $"""
                            INSERT INTO {Qi(tableName)} ({string.Join(", ", insertColumnsSql)})
                            VALUES {string.Join(", ", valuesSql)};
                            """;

            await uow.Connection.ExecuteAsync(new CommandDefinition(
                insertSql,
                p,
                transaction: uow.Transaction,
                cancellationToken: ct));
        }
    }

    private static bool IsCatalogId(string name)
        => string.Equals(name, "catalog_id", StringComparison.OrdinalIgnoreCase);

    private static string Qi(string ident)
    {
        if (string.IsNullOrWhiteSpace(ident))
            throw new NgbArgumentInvalidException(nameof(ident), "Identifier is required.");

        return '"' + ident.Replace("\"", "\"\"") + '"';
    }
}
