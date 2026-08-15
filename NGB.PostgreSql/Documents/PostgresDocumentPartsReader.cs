using Dapper;
using System.Text;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Documents;

internal sealed class PostgresDocumentPartsReader(IUnitOfWork uow) : IDocumentPartsReader
{
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>> GetPartsAsync(
        IReadOnlyList<DocumentTableMetadata> partTables,
        Guid documentId,
        CancellationToken ct = default)
    {
        if (partTables is null)
            throw new NgbArgumentRequiredException(nameof(partTables));

        if (documentId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(documentId));

        if (partTables.Count == 0)
            return new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);

        await uow.EnsureConnectionOpenAsync(ct);

        var result = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
        var queries = new List<DocumentTableMetadata>();
        var sql = new StringBuilder();

        foreach (var t in partTables)
        {
            if (t is null)
                continue;

            if (t.Kind != TableKind.Part)
                continue;

            if (string.IsNullOrWhiteSpace(t.TableName))
                throw new NgbArgumentRequiredException(nameof(DocumentTableMetadata.TableName));

            var cols = t.Columns
                .Where(c => !IsDocumentId(c.ColumnName) && c.Type != ColumnType.Json)
                .Select(c => c.ColumnName)
                .ToList();

            // Part table may contain only technical columns (rare, but keep contract stable).
            if (cols.Count == 0)
            {
                result[t.TableName] = Array.Empty<IReadOnlyDictionary<string, object?>>();
                continue;
            }

            var select = string.Join(",\n       ", cols.Select(c => $"p.{Qi(c)} AS \"{c}\""));
            var orderBy = BuildOrderBy(cols);

            sql.AppendLine($"""
                SELECT {select}
                  FROM {Qi(t.TableName)} p
                 WHERE p.document_id = @documentId
                 {orderBy};
                """);
            queries.Add(t);
        }

        if (queries.Count == 0)
            return result;

        // One command / one network roundtrip for every tabular part. PostgreSQL still executes
        // one SELECT per physical table, but latency and command-count no longer grow with the
        // number of parts. Table and column identifiers come exclusively from trusted metadata
        // and are defensively quoted above.
        using var grid = await uow.Connection.QueryMultipleAsync(new CommandDefinition(
            sql.ToString(),
            new { documentId },
            transaction: uow.Transaction,
            cancellationToken: ct));

        foreach (var table in queries)
        {
            var rows = await grid.ReadAsync();

            var list = new List<IReadOnlyDictionary<string, object?>>();

            foreach (var r in rows)
            {
                var dict = (IDictionary<string, object?>)r;
                list.Add(new Dictionary<string, object?>(dict, StringComparer.OrdinalIgnoreCase));
            }

            result[table.TableName] = list;
        }

        return result;
    }

    private static string BuildOrderBy(IReadOnlyList<string> cols)
    {
        // Try common ordering columns.
        // Keep this heuristic narrow to avoid accidental ordering by arbitrary columns.
        static bool Has(string name, IReadOnlyList<string> list)
            => list.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));

        string? col = null;

        if (Has("ordinal", cols)) col = "ordinal";
        else if (Has("line_no", cols)) col = "line_no";
        else if (Has("entry_no", cols)) col = "entry_no";
        else if (Has("id", cols)) col = "id";

        return col is null ? string.Empty : $"ORDER BY p.{Qi(col)}";
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
