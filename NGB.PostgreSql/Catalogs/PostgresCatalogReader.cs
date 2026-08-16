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

        var where = BuildWhere(head, query);
        var p = where.Params;

        var sql = where.HasHeadCriteria
            ? $"""
               SELECT COUNT(*)
                 FROM {Qi(head.HeadTableName)} h
                 JOIN catalogs c ON c.id = h.catalog_id
                WHERE c.catalog_code = @catalogCode
                  AND ({where.HeadWhereSql});
               """
            : $"""
               SELECT COUNT(*)
                 FROM catalogs c
                WHERE c.catalog_code = @catalogCode
                  AND ({where.CatalogWhereSql});
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

        var where = BuildWhere(head, query);
        var p = where.Params;

        if (!where.HasHeadCriteria)
            return await GetPageWithoutHeadCriteriaAsync(head, where, offset, limit, ct);

        p.Add("catalogCode", head.CatalogCode);
        p.Add("offset", offset);
        p.Add("limit", limit);

        var selectSql = $"""
                        SELECT c.id         AS "Id",
                               c.is_deleted AS "IsDeleted",
                               h.{Qi(head.DisplayColumn)} AS "Display"{BuildSelectFields(head)}
                          FROM {Qi(head.HeadTableName)} h
                          JOIN catalogs c ON c.id = h.catalog_id
                         WHERE c.catalog_code = @catalogCode
                           AND ({where.HeadWhereSql})
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

    private async Task<IReadOnlyList<CatalogHeadRow>> GetPageWithoutHeadCriteriaAsync(
        CatalogHeadDescriptor head,
        CatalogWhere where,
        int offset,
        int limit,
        CancellationToken ct)
    {
        var rows = new List<CatalogHeadRow>(limit);

        var p = where.Params;
        p.Add("catalogCode", head.CatalogCode);
        p.Add("offset", offset);
        p.Add("limit", limit);

        var nonNullHeadSql = $"""
                              SELECT c.id         AS "Id",
                                     c.is_deleted AS "IsDeleted",
                                     h.{Qi(head.DisplayColumn)} AS "Display"{BuildSelectFields(head)}
                                FROM {Qi(head.HeadTableName)} h
                                JOIN catalogs c ON c.id = h.catalog_id
                               WHERE c.catalog_code = @catalogCode
                                 AND ({where.CatalogWhereSql})
                                 AND h.{Qi(head.DisplayColumn)} IS NOT NULL
                               ORDER BY h.{Qi(head.DisplayColumn)}, c.id
                               OFFSET @offset
                                LIMIT @limit;
                              """;

        var nonNullRows = await uow.Connection.QueryAsync(new CommandDefinition(
            nonNullHeadSql,
            p,
            transaction: uow.Transaction,
            cancellationToken: ct));

        foreach (var row in nonNullRows)
        {
            rows.Add(ToRow(head, (IDictionary<string, object?>)row));
        }

        if (rows.Count >= limit)
            return rows;

        var nonNullCountSql = $"""
                               SELECT COUNT(*)
                                 FROM {Qi(head.HeadTableName)} h
                                 JOIN catalogs c ON c.id = h.catalog_id
                                WHERE c.catalog_code = @catalogCode
                                  AND ({where.CatalogWhereSql})
                                  AND h.{Qi(head.DisplayColumn)} IS NOT NULL;
                               """;

        var nonNullCount = await uow.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            nonNullCountSql,
            p,
            transaction: uow.Transaction,
            cancellationToken: ct));

        var nullOffset = Math.Max(0L, offset - nonNullCount);
        var remaining = limit - rows.Count;

        p.Add("nullOffset", nullOffset);
        p.Add("remaining", remaining);

        var nullRowsSql = $"""
                           SELECT *
                             FROM (
                                 SELECT c.id         AS "Id",
                                        c.is_deleted AS "IsDeleted",
                                        h.{Qi(head.DisplayColumn)} AS "Display"{BuildSelectFields(head)}
                                   FROM {Qi(head.HeadTableName)} h
                                   JOIN catalogs c ON c.id = h.catalog_id
                                  WHERE c.catalog_code = @catalogCode
                                    AND ({where.CatalogWhereSql})
                                    AND h.{Qi(head.DisplayColumn)} IS NULL
                                 UNION ALL
                                 SELECT c.id         AS "Id",
                                        c.is_deleted AS "IsDeleted",
                                        NULL::text   AS "Display"{BuildNullSelectFields(head)}
                                   FROM catalogs c
                                  WHERE c.catalog_code = @catalogCode
                                    AND ({where.CatalogWhereSql})
                                    AND NOT EXISTS (
                                        SELECT 1
                                          FROM {Qi(head.HeadTableName)} h
                                         WHERE h.catalog_id = c.id
                                    )
                             ) rows
                            ORDER BY "Id"
                            OFFSET @nullOffset
                             LIMIT @remaining;
                           """;

        var nullRows = await uow.Connection.QueryAsync(new CommandDefinition(
            nullRowsSql,
            p,
            transaction: uow.Transaction,
            cancellationToken: ct));

        rows.AddRange(nullRows.Select(r => ToRow(head, (IDictionary<string, object?>)r)));
        return rows;
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

        var q = (query ?? string.Empty).Trim();
        var hasQuery = q.Length > 0;
        var headDisplaySql = $"h.{Qi(head.DisplayColumn)}";
        var labelSql = $"COALESCE({headDisplaySql}, c.id::text)";
        var sql = hasQuery
            ? $"""
               SELECT c.id AS "Id",
                      {labelSql} AS "Label"
                 FROM catalogs c
                 LEFT JOIN {Qi(head.HeadTableName)} h ON h.catalog_id = c.id
                WHERE c.catalog_code = @catalogCode
                  AND c.is_deleted = FALSE
                  AND {labelSql} ILIKE ('%' || @q::text || '%')
                ORDER BY {headDisplaySql} NULLS LAST, c.updated_at_utc DESC, c.id DESC
                LIMIT @limit;
               """
            : $"""
               SELECT "Id",
                      "Label"
                 FROM (
                     SELECT c.id AS "Id",
                            {labelSql} AS "Label",
                            {headDisplaySql} AS "SortLabel",
                            c.updated_at_utc AS "UpdatedAtUtc"
                       FROM {Qi(head.HeadTableName)} h
                       JOIN catalogs c ON c.id = h.catalog_id
                      WHERE c.catalog_code = @catalogCode
                        AND c.is_deleted = FALSE
                     UNION ALL
                     SELECT c.id AS "Id",
                            c.id::text AS "Label",
                            NULL::text AS "SortLabel",
                            c.updated_at_utc AS "UpdatedAtUtc"
                       FROM catalogs c
                      WHERE c.catalog_code = @catalogCode
                        AND c.is_deleted = FALSE
                        AND NOT EXISTS (
                            SELECT 1
                              FROM {Qi(head.HeadTableName)} h
                             WHERE h.catalog_id = c.id
                        )
                 ) rows
                ORDER BY "SortLabel" NULLS LAST, "UpdatedAtUtc" DESC, "Id" DESC
                LIMIT @limit;
               """;

        var rows = await uow.Connection.QueryAsync<CatalogLookupSqlRow>(new CommandDefinition(
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

        var normalizedQuery = (query ?? string.Empty).Trim();
        var hasQuery = normalizedQuery.Length > 0;

        var p = new DynamicParameters();
        p.Add("perTypeLimit", perTypeLimit, dbType: DbType.Int32);
        if (hasQuery)
            p.Add("q", normalizedQuery, dbType: DbType.String);

        var subqueries = new List<string>(distinctHeads.Length);

        for (var i = 0; i < distinctHeads.Length; i++)
        {
            var head = distinctHeads[i];
            var catalogCodeParam = $"catalogCode{i}";
            p.Add(catalogCodeParam, head.CatalogCode, dbType: DbType.String);

            var activeFilterSql = activeOnly ? "AND c.is_deleted = FALSE" : string.Empty;
            var headDisplaySql = $"h.{Qi(head.DisplayColumn)}";
            var labelSql = $"COALESCE({headDisplaySql}, c.id::text)";
            var fromSql = hasQuery
                ? $"catalogs c LEFT JOIN {Qi(head.HeadTableName)} h ON h.catalog_id = c.id"
                : $"{Qi(head.HeadTableName)} h JOIN catalogs c ON c.id = h.catalog_id";
            var searchFilterSql = hasQuery
                ? $"AND {labelSql} ILIKE ('%' || @q::text || '%')"
                : string.Empty;
            var orderBySql = hasQuery
                ? $"""
                  CASE
                      WHEN {labelSql} ILIKE ('%' || @q::text || '%') THEN 0
                      ELSE 1
                  END,
                  {labelSql},
                  c.id
                  """
                : $"""
                  {headDisplaySql} NULLS LAST,
                  c.id
                  """;

            subqueries.Add($"""
                            (
                                SELECT
                                    c.id AS "Id",
                                    @{catalogCodeParam} AS "CatalogCode",
                                    c.is_deleted AS "IsMarkedForDeletion",
                                    {labelSql} AS "Label"
                                FROM {fromSql}
                                WHERE c.catalog_code = @{catalogCodeParam}
                                  {activeFilterSql}
                                  {searchFilterSql}
                                ORDER BY
                                    {orderBySql}
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

    private static CatalogWhere BuildWhere(CatalogHeadDescriptor head, CatalogQuery query)
    {
        var p = new DynamicParameters();
        
        var headClauses = new List<string>();
        var catalogClauses = new List<string>();
        var hasHeadCriteria = false;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // IMPORTANT: bind the parameter as text explicitly and cast in SQL to avoid 42P08.
            p.Add("search", query.Search.Trim(), dbType: DbType.String);
            headClauses.Add($"h.{Qi(head.DisplayColumn)} ILIKE ('%' || @search::text || '%')");
            hasHeadCriteria = true;
        }

        switch (query.SoftDeleteFilterMode)
        {
            case SoftDeleteFilterMode.Active:
                headClauses.Add("c.is_deleted = FALSE");
                catalogClauses.Add("c.is_deleted = FALSE");
                break;
            case SoftDeleteFilterMode.Deleted:
                headClauses.Add("c.is_deleted = TRUE");
                catalogClauses.Add("c.is_deleted = TRUE");
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
                headClauses.Add($"h.{Qi(f.ColumnName)}::text = @f{i}");
                hasHeadCriteria = true;
                i++;
            }
        }

        return new CatalogWhere(
            HeadWhereSql: headClauses.Count == 0 ? "TRUE" : string.Join(" AND ", headClauses),
            CatalogWhereSql: catalogClauses.Count == 0 ? "TRUE" : string.Join(" AND ", catalogClauses),
            Params: p,
            HasHeadCriteria: hasHeadCriteria);
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

    private static string BuildNullSelectFields(CatalogHeadDescriptor head)
    {
        var cols = head.Columns
            .Where(c => !string.Equals(c.ColumnName, head.DisplayColumn, StringComparison.OrdinalIgnoreCase))
            .Select(c => $",\n       NULL AS \"{c.ColumnName}\"")
            .ToList();

        cols.Insert(0, $",\n       NULL AS \"{head.DisplayColumn}\"");

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

    private sealed class CatalogLookupSqlRow
    {
        public Guid Id { get; init; }
        public string Label { get; init; } = null!;
    }

    private sealed record CatalogWhere(
        string HeadWhereSql,
        string CatalogWhereSql,
        DynamicParameters Params,
        bool HasHeadCriteria);

    private static string Qi(string ident)
    {
        if (string.IsNullOrWhiteSpace(ident))
            throw new NgbArgumentInvalidException(nameof(ident), "Identifier is required.");

        // Identifiers are sourced from trusted metadata (Definitions). Quote defensively.
        return '"' + ident.Replace("\"", "\"\"") + '"';
    }
}
