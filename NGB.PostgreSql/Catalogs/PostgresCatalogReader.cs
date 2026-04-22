using System.Data;
using Dapper;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.Common;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Catalogs;

internal sealed class PostgresCatalogReader(IUnitOfWork uow) : ICatalogReader
{
    public async Task<long> CountAsync(CatalogHeadDescriptor head, CatalogQuery query, CancellationToken ct = default)
    {
        EnsureValid(head);
        await uow.EnsureConnectionOpenAsync(ct);

        var (whereSql, p) = BuildWhere(head, query);

        var sql = $"""
                  SELECT COUNT(*)
                    FROM catalogs c
                    LEFT JOIN {Qi(head.HeadTableName)} h ON h.catalog_id = c.id
                   WHERE c.catalog_code = @catalogCode
                     AND ({whereSql});
                  """;

        p.Add("catalogCode", head.CatalogCode);

        return await uow.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            sql,
            p,
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CatalogHeadRow>> GetPageAsync(
        CatalogHeadDescriptor head,
        CatalogQuery query,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        EnsureValid(head);
        
        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Argument is out of range.");
        
        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Argument is out of range.");

        await uow.EnsureConnectionOpenAsync(ct);

        var (whereSql, p) = BuildWhere(head, query);
        p.Add("catalogCode", head.CatalogCode);
        p.Add("offset", offset);
        p.Add("limit", limit);

        var selectSql = $"""
                         SELECT c.id         AS "Id",
                                c.is_deleted AS "IsDeleted",
                                h.{Qi(head.DisplayColumn)} AS "Display"{BuildSelectFields(head)}
                           FROM catalogs c
                           LEFT JOIN {Qi(head.HeadTableName)} h ON h.catalog_id = c.id
                          WHERE c.catalog_code = @catalogCode
                            AND ({whereSql})
                          ORDER BY h.{Qi(head.DisplayColumn)} NULLS LAST, c.id
                          OFFSET @offset
                           LIMIT @limit;
                         """;

        var rows = await uow.Connection.QueryAsync(new CommandDefinition(
            selectSql,
            p,
            transaction: uow.Transaction,
            cancellationToken: ct));

        return rows
            .Select(r => ToRow(head, (IDictionary<string, object?>)r))
            .ToList();
    }

    public async Task<CatalogHeadRow?> GetByIdAsync(
        CatalogHeadDescriptor head,
        Guid id,
        CancellationToken ct = default)
    {
        EnsureValid(head);
        
        if (id == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(id));

        await uow.EnsureConnectionOpenAsync(ct);

        var sql = $"""
                  SELECT c.id         AS "Id",
                         c.is_deleted AS "IsDeleted",
                         h.{Qi(head.DisplayColumn)} AS "Display"{BuildSelectFields(head)}
                    FROM catalogs c
                    LEFT JOIN {Qi(head.HeadTableName)} h ON h.catalog_id = c.id
                   WHERE c.catalog_code = @catalogCode
                     AND c.id          = @id;
                  """;

        var row = await uow.Connection.QuerySingleOrDefaultAsync(new CommandDefinition(
            sql,
            new { catalogCode = head.CatalogCode, id },
            transaction: uow.Transaction,
            cancellationToken: ct));

        return row is null ? null : ToRow(head, (IDictionary<string, object?>)row);
    }

    public async Task<IReadOnlyList<CatalogLookupRow>> LookupAsync(
        CatalogHeadDescriptor head,
        string? query,
        int limit,
        CancellationToken ct = default)
    {
        EnsureValid(head);
        if (limit <= 0) return [];

        await uow.EnsureConnectionOpenAsync(ct);

        var q = query ?? string.Empty;

        var sql = $"""
                  SELECT c.id AS "Id",
                         COALESCE(h.{Qi(head.DisplayColumn)}, c.id::text) AS "Label"
                    FROM catalogs c
                    LEFT JOIN {Qi(head.HeadTableName)} h ON h.catalog_id = c.id
                   WHERE c.catalog_code = @catalogCode
                     AND c.is_deleted = FALSE
                     AND (@q = '' OR h.{Qi(head.DisplayColumn)} ILIKE ('%' || @q || '%'))
                   ORDER BY h.{Qi(head.DisplayColumn)} NULLS LAST, c.updated_at_utc DESC, c.id DESC
                   LIMIT @limit;
                  """;

        var rows = await uow.Connection.QueryAsync<(Guid Id, string Label)>(new CommandDefinition(
            sql,
            new { catalogCode = head.CatalogCode, q, limit },
            transaction: uow.Transaction,
            cancellationToken: ct));

        return rows.Select(x => new CatalogLookupRow(x.Id, x.Label)).ToList();
    }

    public async Task<IReadOnlyList<CatalogLookupRow>> GetByIdsAsync(
        CatalogHeadDescriptor head,
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default)
    {
        EnsureValid(head);
        
        if (ids.Count == 0)
            return [];

        await uow.EnsureConnectionOpenAsync(ct);

        var sql = $"""
                  SELECT c.id AS "Id",
                         COALESCE(h.{Qi(head.DisplayColumn)}, c.id::text) AS "Label"
                    FROM catalogs c
                    LEFT JOIN {Qi(head.HeadTableName)} h ON h.catalog_id = c.id
                   WHERE c.catalog_code = @catalogCode
                     AND c.id = ANY(@ids);
                  """;

        var rows = await uow.Connection.QueryAsync<(Guid Id, string Label)>(new CommandDefinition(
            sql,
            new { catalogCode = head.CatalogCode, ids = ids.ToArray() },
            transaction: uow.Transaction,
            cancellationToken: ct));

        var dict = rows.ToDictionary(x => x.Id, x => x.Label);

        return ids
            .Where(dict.ContainsKey)
            .Select(id => new CatalogLookupRow(id, dict[id]))
            .ToList();
    }

    public async Task<IReadOnlyList<CatalogLookupSearchRow>> LookupAcrossTypesAsync(
        IReadOnlyList<CatalogHeadDescriptor> heads,
        string? query,
        int perTypeLimit,
        bool activeOnly,
        CancellationToken ct = default)
    {
        if (heads is null)
            throw new NgbArgumentRequiredException(nameof(heads));

        if (perTypeLimit <= 0 || heads.Count == 0)
            return [];

        var distinctHeads = heads
            .Where(static head => head is not null)
            .GroupBy(head => head.CatalogCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (distinctHeads.Length == 0)
            return [];

        foreach (var head in distinctHeads)
        {
            EnsureValid(head);
        }

        await uow.EnsureConnectionOpenAsync(ct);

        var p = new DynamicParameters();
        p.Add("q", (query ?? string.Empty).Trim(), dbType: DbType.String);
        p.Add("perTypeLimit", perTypeLimit, dbType: DbType.Int32);

        var subqueries = new List<string>(distinctHeads.Length);

        for (var i = 0; i < distinctHeads.Length; i++)
        {
            var head = distinctHeads[i];
            var catalogCodeParam = $"catalogCode{i}";
            p.Add(catalogCodeParam, head.CatalogCode, dbType: DbType.String);

            var activeFilterSql = activeOnly ? "AND c.is_deleted = FALSE" : string.Empty;
            var displaySql = $"COALESCE(h.{Qi(head.DisplayColumn)}, c.id::text)";

            subqueries.Add($"""
                            (
                                SELECT
                                    c.id AS "Id",
                                    @{catalogCodeParam} AS "CatalogCode",
                                    c.is_deleted AS "IsMarkedForDeletion",
                                    {displaySql} AS "Label"
                                FROM catalogs c
                                LEFT JOIN {Qi(head.HeadTableName)} h ON h.catalog_id = c.id
                                WHERE c.catalog_code = @{catalogCodeParam}
                                  {activeFilterSql}
                                  AND (@q = '' OR {displaySql} ILIKE ('%' || @q::text || '%'))
                                ORDER BY
                                    CASE
                                        WHEN {displaySql} ILIKE ('%' || @q::text || '%') THEN 0
                                        ELSE 1
                                    END,
                                    {displaySql},
                                    c.id
                                LIMIT @perTypeLimit
                            )
                            """);
        }

        var sql = string.Join("\nUNION ALL\n", subqueries);

        var rows = await uow.Connection.QueryAsync<CatalogLookupSearchSqlRow>(new CommandDefinition(
            sql,
            p,
            transaction: uow.Transaction,
            cancellationToken: ct));

        return rows
            .Select(row => new CatalogLookupSearchRow(
                row.Id,
                row.CatalogCode,
                row.Label,
                row.IsMarkedForDeletion))
            .ToList();
    }

    private static void EnsureValid(CatalogHeadDescriptor head)
    {
        if (string.IsNullOrWhiteSpace(head.CatalogCode))
            throw new NgbArgumentRequiredException(nameof(head.CatalogCode));
        
        if (string.IsNullOrWhiteSpace(head.HeadTableName))
            throw new NgbArgumentRequiredException(nameof(head.HeadTableName));
        
        if (string.IsNullOrWhiteSpace(head.DisplayColumn))
            throw new NgbArgumentRequiredException(nameof(head.DisplayColumn));
    }

    private static (string WhereSql, DynamicParameters Params) BuildWhere(CatalogHeadDescriptor head, CatalogQuery query)
    {
        var p = new DynamicParameters();
        
        // IMPORTANT: when the search value is NULL, PostgreSQL can't infer the parameter type in
        // expressions like ("%" || @search || "%"). Bind the parameter as text explicitly and also
        // cast in SQL below to avoid 42P08.
        p.Add("search", string.IsNullOrWhiteSpace(query.Search) ? null : query.Search, dbType: DbType.String);

        var clauses = new List<string>
        {
            $"(@search IS NULL OR h.{Qi(head.DisplayColumn)} ILIKE ('%' || @search::text || '%'))"
        };

        switch (query.SoftDeleteFilterMode)
        {
            case SoftDeleteFilterMode.Active:
                clauses.Add("c.is_deleted = FALSE");
                break;
            case SoftDeleteFilterMode.Deleted:
                clauses.Add("c.is_deleted = TRUE");
                break;
            case SoftDeleteFilterMode.All:
            default:
                break;
        }

        if (query.Filters is { Count: > 0 })
        {
            var i = 0;
            foreach (var f in query.Filters)
            {
                p.Add($"f{i}", f.Value, dbType: DbType.String);
                clauses.Add($"h.{Qi(f.ColumnName)}::text = @f{i}");
                i++;
            }
        }

        return (string.Join(" AND ", clauses), p);
    }

    private static string BuildSelectFields(CatalogHeadDescriptor head)
    {
        var cols = head.Columns
            .Where(c => !string.Equals(c.ColumnName, head.DisplayColumn, StringComparison.OrdinalIgnoreCase))
            .Select(c => $",\n       h.{Qi(c.ColumnName)} AS \"{c.ColumnName}\"")
            .ToList();

        // Include the display column also as a field.
        cols.Insert(0, $",\n       h.{Qi(head.DisplayColumn)} AS \"{head.DisplayColumn}\"");

        return string.Concat(cols);
    }

    private static CatalogHeadRow ToRow(CatalogHeadDescriptor head, IDictionary<string, object?> row)
    {
        var id = (Guid)row["Id"]!;
        var isDeleted = (bool)row["IsDeleted"]!;
        var display = row.TryGetValue("Display", out var d) ? d as string : null;

        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in head.Columns)
        {
            row.TryGetValue(col.ColumnName, out var value);
            fields[col.ColumnName] = value;
        }

        return new CatalogHeadRow(id, isDeleted, display, fields);
    }

    private sealed class CatalogLookupSearchSqlRow
    {
        public Guid Id { get; init; }
        public string CatalogCode { get; init; } = null!;
        public string? Label { get; init; }
        public bool IsMarkedForDeletion { get; init; }
    }

    private static string Qi(string ident)
    {
        if (string.IsNullOrWhiteSpace(ident))
            throw new NgbArgumentInvalidException(nameof(ident), "Identifier is required.");

        // Identifiers are sourced from trusted metadata (Definitions). Quote defensively.
        return '"' + ident.Replace("\"", "\"\"") + '"';
    }
}
