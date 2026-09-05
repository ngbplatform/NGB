using Dapper;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Catalogs;

internal sealed class PostgresCatalogPartsWriter(IUnitOfWork uow) : ICatalogPartsWriter
{
    private const int MaxParametersPerBatch = 2000;
    private const int MaxRowsPerBatch = 500;

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

        var preparedParts = new List<PreparedPart>();
        foreach (var t in partTables)
        {
            if (t is null)
                continue;

            if (t.Kind != TableKind.Part)
                continue;

            var tableName = t.TableName;
            if (string.IsNullOrWhiteSpace(tableName))
                throw new NgbArgumentInvalidException(nameof(partTables), "Part table name is required.");

            rowsByTable.TryGetValue(tableName, out var rows);
            rows ??= [];

            if (rows.Count == 0)
            {
                preparedParts.Add(new PreparedPart(tableName, rows, []));
                continue;
            }

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

            preparedParts.Add(new PreparedPart(tableName, rows, orderedColumns));
        }

        if (preparedParts.Count == 0)
            return;

        var deleteSql = string.Join(
            Environment.NewLine,
            preparedParts.Select(part => $"DELETE FROM {Qi(part.TableName)} WHERE catalog_id = @catalogId;"));
        var pendingSql = new List<string> { deleteSql };
        var pendingParameters = new DynamicParameters();
        pendingParameters.Add("catalogId", catalogId);
        var pendingParameterCount = 0;
        var statementIndex = 0;

        async Task FlushAsync()
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                string.Join(Environment.NewLine, pendingSql),
                pendingParameters,
                transaction: uow.Transaction,
                cancellationToken: ct));

            pendingSql = [];
            pendingParameters = new DynamicParameters();
            pendingParameters.Add("catalogId", catalogId);
            pendingParameterCount = 0;
        }

        foreach (var part in preparedParts)
        {
            if (part.Rows.Count == 0)
                continue;

            var insertColumnsSql = new List<string> { "catalog_id" };
            insertColumnsSql.AddRange(part.OrderedColumns.Select(Qi));

            var batchSize = Math.Clamp(MaxParametersPerBatch / part.OrderedColumns.Count, 1, MaxRowsPerBatch);

            for (var offset = 0; offset < part.Rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, part.Rows.Count - offset);
                var requiredParameters = take * part.OrderedColumns.Count;

                if (pendingParameterCount > 0 && pendingParameterCount + requiredParameters > MaxParametersPerBatch)
                    await FlushAsync();

                var valuesSql = new List<string>(take);
                var currentStatementIndex = statementIndex++;

                for (var batchIndex = 0; batchIndex < take; batchIndex++)
                {
                    var row = part.Rows[offset + batchIndex];
                    var rowParams = new List<string> { "@catalogId" };

                    for (var columnIndex = 0; columnIndex < part.OrderedColumns.Count; columnIndex++)
                    {
                        var col = part.OrderedColumns[columnIndex];
                        var paramName = $"p_{currentStatementIndex}_{batchIndex}_{columnIndex}";
                        row.TryGetValue(col, out var value);
                        pendingParameters.Add(paramName, value);
                        rowParams.Add("@" + paramName);
                    }

                    valuesSql.Add("(" + string.Join(", ", rowParams) + ")");
                }

                pendingSql.Add($"""
                                INSERT INTO {Qi(part.TableName)} ({string.Join(", ", insertColumnsSql)})
                                VALUES {string.Join(", ", valuesSql)};
                                """);
                pendingParameterCount += requiredParameters;
            }
        }

        await FlushAsync();
    }

    private sealed record PreparedPart(
        string TableName,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
        IReadOnlyList<string> OrderedColumns);

    private static bool IsCatalogId(string name) => string.Equals(name, "catalog_id", StringComparison.OrdinalIgnoreCase);

    private static string Qi(string ident)
    {
        if (string.IsNullOrWhiteSpace(ident))
            throw new NgbArgumentInvalidException(nameof(ident), "Identifier is required.");

        return '"' + ident.Replace("\"", "\"\"") + '"';
    }
}
