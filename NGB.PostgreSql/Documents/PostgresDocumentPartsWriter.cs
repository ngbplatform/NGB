using Dapper;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Documents;

internal sealed class PostgresDocumentPartsWriter(IUnitOfWork uow) : IDocumentPartsWriter
{
    private const int MaxParametersPerBatch = 2000;
    private const int MaxRowsPerBatch = 500;

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
            rows ??= Array.Empty<IReadOnlyDictionary<string, object?>>();

            if (rows.Count == 0)
            {
                preparedParts.Add(new PreparedPart(tableName, rows, []));
                continue;
            }

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

            preparedParts.Add(new PreparedPart(tableName, rows, orderedColumns));
        }

        if (preparedParts.Count == 0)
            return;

        var deleteSql = string.Join(
            Environment.NewLine,
            preparedParts.Select(part => $"DELETE FROM {Qi(part.TableName)} WHERE document_id = @documentId;"));
        var pendingSql = new List<string> { deleteSql };
        var pendingParameters = new DynamicParameters();
        pendingParameters.Add("documentId", documentId);
        var pendingParameterCount = 0;
        var statementIndex = 0;

        async Task FlushAsync()
        {
            if (pendingSql.Count == 0)
                return;

            await uow.Connection.ExecuteAsync(new CommandDefinition(
                string.Join(Environment.NewLine, pendingSql),
                pendingParameters,
                transaction: uow.Transaction,
                cancellationToken: ct));

            pendingSql = [];
            pendingParameters = new DynamicParameters();
            pendingParameters.Add("documentId", documentId);
            pendingParameterCount = 0;
        }

        foreach (var part in preparedParts)
        {
            if (part.Rows.Count == 0)
                continue;

            var insertColumnsSql = new List<string> { "document_id" };
            insertColumnsSql.AddRange(part.OrderedColumns.Select(Qi));

            var batchSize = Math.Clamp(MaxParametersPerBatch / part.OrderedColumns.Count, 1, MaxRowsPerBatch);

            for (var offset = 0; offset < part.Rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, part.Rows.Count - offset);
                var requiredParameters = take * part.OrderedColumns.Count;
                if (pendingParameterCount > 0
                    && pendingParameterCount + requiredParameters > MaxParametersPerBatch)
                {
                    await FlushAsync();
                }

                var valuesSql = new List<string>(take);
                var currentStatementIndex = statementIndex++;

                for (var batchIndex = 0; batchIndex < take; batchIndex++)
                {
                    var row = part.Rows[offset + batchIndex];
                    var rowParams = new List<string> { "@documentId" };

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
