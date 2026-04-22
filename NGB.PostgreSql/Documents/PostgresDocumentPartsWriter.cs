using Dapper;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Documents;

internal sealed class PostgresDocumentPartsWriter(IUnitOfWork uow) : IDocumentPartsWriter
{
    public async Task ReplacePartsAsync(
        IReadOnlyList<DocumentTableMetadata> partTables,
        Guid documentId,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> rowsByTable,
        CancellationToken ct = default)
    {
        if (documentId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(documentId));

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

            // Replace semantics: wipe rows for document_id, then insert.
            var deleteSql = $"DELETE FROM {Qi(tableName)} WHERE document_id = @documentId;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                deleteSql,
                new { documentId },
                transaction: uow.Transaction,
                cancellationToken: ct));

            rowsByTable.TryGetValue(tableName, out var rows);
            rows ??= Array.Empty<IReadOnlyDictionary<string, object?>>();

            if (rows.Count == 0)
                continue;

            var allowed = t.Columns
                .Where(c => !IsDocumentId(c.ColumnName) && c.Type != ColumnType.Json)
                .Select(c => c.ColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Determine columns to insert from provided rows.
            var usedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                if (r is null)
                    throw new NgbArgumentInvalidException(nameof(rowsByTable), $"Null row is not allowed for '{tableName}'.");

                foreach (var k in r.Keys)
                {
                    if (IsDocumentId(k))
                        throw new NgbArgumentInvalidException(nameof(rowsByTable), $"'document_id' must not be provided for '{tableName}'.");

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

            var insertColumnsSql = new List<string> { "document_id" };
            insertColumnsSql.AddRange(orderedColumns.Select(Qi));

            var p = new DynamicParameters();
            p.Add("documentId", documentId);

            var valuesSql = new List<string>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var rowParams = new List<string> { "@documentId" };

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

    private static bool IsDocumentId(string name)
        => string.Equals(name, "document_id", StringComparison.OrdinalIgnoreCase);

    private static string Qi(string ident)
    {
        if (string.IsNullOrWhiteSpace(ident))
            throw new NgbArgumentInvalidException(nameof(ident), "Identifier is required.");

        // Identifiers are sourced from trusted metadata (Definitions). Quote defensively.
        return '"' + ident.Replace("\"", "\"\"") + '"';
    }
}
