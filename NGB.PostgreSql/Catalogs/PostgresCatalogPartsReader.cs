using Dapper;
using System.Text;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Catalogs;

internal sealed class PostgresCatalogPartsReader(IUnitOfWork uow) : ICatalogPartsReader
{
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>> GetPartsAsync(
        IReadOnlyList<CatalogTableMetadata> partTables,
        Guid catalogId,
        CancellationToken ct = default)
    {
        if (partTables is null)
            throw new NgbArgumentRequiredException(nameof(partTables));

        if (catalogId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(catalogId));

        if (partTables.Count == 0)
            return new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);

        await uow.EnsureConnectionOpenAsync(ct);

        var result = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
        var queries = new List<CatalogTableMetadata>();
        var sql = new StringBuilder();

        foreach (var t in partTables)
        {
            if (t is null)
                continue;

            if (t.Kind != TableKind.Part)
                continue;

            if (string.IsNullOrWhiteSpace(t.TableName))
                throw new NgbArgumentRequiredException(nameof(CatalogTableMetadata.TableName));

            var cols = t.Columns
                .Where(c => !IsCatalogId(c.ColumnName) && c.ColumnType != ColumnType.Json)
                .Select(c => c.ColumnName)
                .ToList();

            if (cols.Count == 0)
            {
                result[t.TableName] = [];
                continue;
            }

            var select = string.Join(",\n       ", cols.Select(c => $"p.{Qi(c)} AS \"{c}\""));
            var orderBy = BuildOrderBy(cols);

            sql.AppendLine($"""
                SELECT {select}
                  FROM {Qi(t.TableName)} p
                 WHERE p.catalog_id = @catalogId
                 {orderBy};
                """);
            queries.Add(t);
        }

        if (queries.Count == 0)
            return result;

        await using var grid = await uow.Connection.QueryMultipleAsync(new CommandDefinition(
            sql.ToString(),
            new { catalogId },
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
        static bool Has(string name, IReadOnlyList<string> list)
            => list.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));

        string? col = null;

        if (Has("ordinal", cols)) col = "ordinal";
        else if (Has("line_no", cols)) col = "line_no";
        else if (Has("entry_no", cols)) col = "entry_no";
        else if (Has("id", cols)) col = "id";

        return col is null ? string.Empty : $"ORDER BY p.{Qi(col)}";
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
