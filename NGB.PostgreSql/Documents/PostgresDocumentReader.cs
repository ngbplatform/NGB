using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using NGB.Contracts.Common;
using NGB.Core.Documents;
using NGB.Metadata.Base;
using NGB.Persistence.Common;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Search;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Documents;

internal sealed class PostgresDocumentReader(
    IUnitOfWork uow,
    IEnumerable<IPostgresDocumentListFilterSqlContributor> filterSqlContributors)
    : IDocumentCombinedPageReader
{
    public async Task<long> CountAsync(DocumentHeadDescriptor head, DocumentQuery query, CancellationToken ct = default)
    {
        EnsureValid(head);
        await uow.EnsureConnectionOpenAsync(ct);

        var where = BuildWhere(head, query);
        var p = where.Params;
        var sql = where.HasHeadCriteria
            ? $"""
               SELECT COUNT(*)
                 FROM {Qi(head.HeadTableName)} h
                 JOIN documents d ON d.id = h.document_id
                WHERE d.type_code = @typeCode
                  AND ({where.Sql});
               """
            : $"""
               SELECT COUNT(*)
                 FROM documents d
                WHERE d.type_code = @typeCode
                  AND ({where.Sql});
               """;

        p.Add("typeCode", head.TypeCode);

        return await uow.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            sql,
            p,
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<DocumentHeadRow>> GetPageAsync(
        DocumentHeadDescriptor head,
        DocumentQuery query,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        EnsureValid(head);

        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Argument is out of range.");

        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Argument is out of range.");

        offset = PagingLimits.BoundOffset(offset);

        await uow.EnsureConnectionOpenAsync(ct);

        var where = BuildWhere(head, query);
        var p = where.Params;

        if (!where.HasHeadCriteria)
            return await GetPageWithoutHeadCriteriaAsync(head, where, offset, limit, ct);

        p.Add("typeCode", head.TypeCode);
        p.Add("offset", offset);
        p.Add("limit", limit);

        var selectSql = $"""
                         SELECT d.id     AS "Id",
                                d.status AS "Status",
                                d.number AS "Number",
                                COALESCE(h.{Qi(head.DisplayColumn)}, d.id::text) AS "Display"{BuildSelectFields(head)}
                           FROM {Qi(head.HeadTableName)} h
                           JOIN documents d ON d.id = h.document_id
                          WHERE d.type_code = @typeCode
                            AND ({where.Sql})
                          ORDER BY h.{Qi(head.DisplayColumn)} NULLS LAST, d.id
                          OFFSET @offset
                           LIMIT @limit;
                         """;

        var rows = await uow.Connection.QueryAsync(new CommandDefinition(
            selectSql,
            p,
            transaction: uow.Transaction,
            cancellationToken: ct));

        var result = new List<DocumentHeadRow>();
        foreach (var row in rows)
        {
            result.Add(ToRow(head, (IDictionary<string, object?>)row));
        }

        return result;
    }

    public async Task<DocumentHeadQueryPage> GetPageWithTotalAsync(
        DocumentHeadDescriptor head,
        DocumentQuery query,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        EnsureValid(head);

        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Argument is out of range.");

        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Argument is out of range.");

        offset = PagingLimits.BoundOffset(offset);

        await uow.EnsureConnectionOpenAsync(ct);

        var where = BuildWhere(head, query);
        var parameters = where.Params;
        parameters.Add("typeCode", head.TypeCode);
        parameters.Add("offset", offset);
        parameters.Add("limit", limit);

        var countSql = where.HasHeadCriteria
            ? $"""
               SELECT COUNT(*)
                 FROM {Qi(head.HeadTableName)} h
                 JOIN documents d ON d.id = h.document_id
                WHERE d.type_code = @typeCode
                  AND ({where.Sql});
               """
            : $"""
               SELECT COUNT(*)
                 FROM documents d
                WHERE d.type_code = @typeCode
                  AND ({where.Sql});
               """;

        var pageSql = where.HasHeadCriteria
            ? $"""
               SELECT d.id     AS "Id",
                      d.status AS "Status",
                      d.number AS "Number",
                      COALESCE(h.{Qi(head.DisplayColumn)}, d.id::text) AS "Display"{BuildSelectFields(head)}
                 FROM {Qi(head.HeadTableName)} h
                 JOIN documents d ON d.id = h.document_id
                WHERE d.type_code = @typeCode
                  AND ({where.Sql})
                ORDER BY h.{Qi(head.DisplayColumn)} NULLS LAST, d.id
                OFFSET @offset
                 LIMIT @limit;
               """
            : $"""
               SELECT *
                 FROM (
                     SELECT d.id     AS "Id",
                            d.status AS "Status",
                            d.number AS "Number",
                            COALESCE(h.{Qi(head.DisplayColumn)}, d.id::text) AS "Display",
                            h.{Qi(head.DisplayColumn)} AS "SortDisplay"{BuildSelectFields(head)}
                       FROM {Qi(head.HeadTableName)} h
                       JOIN documents d ON d.id = h.document_id
                      WHERE d.type_code = @typeCode
                        AND ({where.Sql})
                     UNION ALL
                     SELECT d.id     AS "Id",
                            d.status AS "Status",
                            d.number AS "Number",
                            d.id::text AS "Display",
                            NULL::text AS "SortDisplay"{BuildNullSelectFields(head)}
                       FROM documents d
                      WHERE d.type_code = @typeCode
                        AND ({where.Sql})
                        AND NOT EXISTS (
                            SELECT 1
                              FROM {Qi(head.HeadTableName)} h
                             WHERE h.document_id = d.id
                        )
                 ) rows
                ORDER BY "SortDisplay" NULLS LAST, "Id"
                OFFSET @offset
                 LIMIT @limit;
               """;

        await using var results = await uow.Connection.QueryMultipleAsync(new CommandDefinition(
            $"{countSql}\n{pageSql}",
            parameters,
            transaction: uow.Transaction,
            cancellationToken: ct));

        var total = await results.ReadSingleAsync<long>();
        var rows = (await results.ReadAsync())
            .Select(row => ToRow(head, (IDictionary<string, object?>)row))
            .ToArray();

        return new DocumentHeadQueryPage(rows, total);
    }

    private async Task<IReadOnlyList<DocumentHeadRow>> GetPageWithoutHeadCriteriaAsync(
        DocumentHeadDescriptor head,
        DocumentWhere where,
        int offset,
        int limit,
        CancellationToken ct)
    {
        var rows = new List<DocumentHeadRow>(limit);

        var p = where.Params;
        p.Add("typeCode", head.TypeCode);
        p.Add("offset", offset);
        p.Add("limit", limit);

        var nonNullHeadSql = $"""
                              SELECT d.id     AS "Id",
                                     d.status AS "Status",
                                     d.number AS "Number",
                                     h.{Qi(head.DisplayColumn)} AS "Display"{BuildSelectFields(head)}
                                FROM {Qi(head.HeadTableName)} h
                                JOIN documents d ON d.id = h.document_id
                               WHERE d.type_code = @typeCode
                                 AND ({where.Sql})
                                 AND h.{Qi(head.DisplayColumn)} IS NOT NULL
                               ORDER BY h.{Qi(head.DisplayColumn)}, d.id
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
                                 JOIN documents d ON d.id = h.document_id
                                WHERE d.type_code = @typeCode
                                  AND ({where.Sql})
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
                                 SELECT d.id     AS "Id",
                                        d.status AS "Status",
                                        d.number AS "Number",
                                        d.id::text AS "Display"{BuildSelectFields(head)}
                                   FROM {Qi(head.HeadTableName)} h
                                   JOIN documents d ON d.id = h.document_id
                                  WHERE d.type_code = @typeCode
                                    AND ({where.Sql})
                                    AND h.{Qi(head.DisplayColumn)} IS NULL
                                 UNION ALL
                                 SELECT d.id     AS "Id",
                                        d.status AS "Status",
                                        d.number AS "Number",
                                        d.id::text AS "Display"{BuildNullSelectFields(head)}
                                   FROM documents d
                                  WHERE d.type_code = @typeCode
                                    AND ({where.Sql})
                                    AND NOT EXISTS (
                                        SELECT 1
                                          FROM {Qi(head.HeadTableName)} h
                                         WHERE h.document_id = d.id
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

    public async Task<DocumentHeadRow?> GetByIdAsync(
        DocumentHeadDescriptor head,
        Guid id,
        CancellationToken ct = default)
    {
        EnsureValid(head);

        if (id == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(id));

        await uow.EnsureConnectionOpenAsync(ct);

        var sql = $"""
                  SELECT d.id     AS "Id",
                         d.status AS "Status",
                         d.number AS "Number",
                         COALESCE(h.{Qi(head.DisplayColumn)}, d.id::text) AS "Display"{BuildSelectFields(head)}
                    FROM documents d
                    LEFT JOIN {Qi(head.HeadTableName)} h ON h.document_id = d.id
                   WHERE d.type_code = @typeCode
                     AND d.id        = @id;
                  """;

        var row = await uow.Connection.QuerySingleOrDefaultAsync(new CommandDefinition(
            sql,
            new { typeCode = head.TypeCode, id },
            transaction: uow.Transaction,
            cancellationToken: ct));

        return row is null ? null : ToRow(head, (IDictionary<string, object?>)row);
    }

    public async Task<IReadOnlyList<DocumentHeadRow>> GetByIdsAsync(
        DocumentHeadDescriptor head,
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default)
    {
        EnsureValid(head);

        if (ids.Count == 0)
            return [];

        var uniq = ids.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (uniq.Length == 0)
            return [];

        await uow.EnsureConnectionOpenAsync(ct);

        var sql = $"""
                  SELECT d.id     AS "Id",
                         d.status AS "Status",
                         d.number AS "Number",
                         COALESCE(h.{Qi(head.DisplayColumn)}, d.id::text) AS "Display"{BuildSelectFields(head)}
                    FROM documents d
                    LEFT JOIN {Qi(head.HeadTableName)} h ON h.document_id = d.id
                   WHERE d.type_code = @typeCode
                     AND d.id = ANY(@ids);
                  """;

        var rows = await uow.Connection.QueryAsync(new CommandDefinition(
            sql,
            new { typeCode = head.TypeCode, ids = uniq },
            transaction: uow.Transaction,
            cancellationToken: ct));

        var result = new List<DocumentHeadRow>();
        foreach (var row in rows)
        {
            result.Add(ToRow(head, (IDictionary<string, object?>)row));
        }

        return result;
    }

    public async Task<IReadOnlyList<DocumentHeadRow>> GetHeadRowsByIdsAcrossTypesAsync(
        IReadOnlyList<DocumentHeadDescriptor> heads,
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default)
    {
        if (heads is null)
            throw new NgbArgumentRequiredException(nameof(heads));

        if (ids is null)
            throw new NgbArgumentRequiredException(nameof(ids));

        if (heads.Count == 0 || ids.Count == 0)
            return [];

        var distinctHeads = heads
            .Where(static head => head is not null)
            .GroupBy(head => head.TypeCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (distinctHeads.Length == 0)
            return [];

        var uniqIds = ids.Where(static id => id != Guid.Empty).Distinct().ToArray();
        if (uniqIds.Length == 0)
            return [];

        foreach (var head in distinctHeads)
        {
            EnsureValid(head);
        }

        await uow.EnsureConnectionOpenAsync(ct);

        var p = new DynamicParameters();
        p.Add("ids", uniqIds);

        var subqueries = new List<string>(distinctHeads.Length);

        for (var i = 0; i < distinctHeads.Length; i++)
        {
            var head = distinctHeads[i];
            var typeCodeParam = $"typeCode{i}";
            p.Add(typeCodeParam, head.TypeCode, dbType: DbType.String);

            var displaySql = $"COALESCE(h.{Qi(head.DisplayColumn)}, d.id::text)";
            subqueries.Add($"""
                            (
                                SELECT
                                    d.id AS "Id",
                                    @{typeCodeParam} AS "TypeCode",
                                    d.status AS "Status",
                                    d.number AS "Number",
                                    {displaySql} AS "Display",
                                    {BuildFieldsJson(head)}::text AS "FieldsJson"
                                FROM documents d
                                LEFT JOIN {Qi(head.HeadTableName)} h ON h.document_id = d.id
                                WHERE d.type_code = @{typeCodeParam}
                                  AND d.id = ANY(@ids)
                            )
                            """);
        }

        var sql = string.Join("\nUNION ALL\n", subqueries);
        var headByType = distinctHeads.ToDictionary(x => x.TypeCode, StringComparer.OrdinalIgnoreCase);

        var rows = await uow.Connection.QueryAsync<DocumentHeadSqlRow>(new CommandDefinition(
            sql,
            p,
            transaction: uow.Transaction,
            cancellationToken: ct));

        return rows
            .Select(row => ToHeadRow(headByType[row.TypeCode], row))
            .ToList();
    }

    public async Task<IReadOnlyList<DocumentLookupRow>> LookupAcrossTypesAsync(
        IReadOnlyList<DocumentHeadDescriptor> heads,
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
            .GroupBy(head => head.TypeCode, StringComparer.OrdinalIgnoreCase)
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
        var queryId = Guid.TryParse(normalizedQuery, out var parsedQueryId) ? parsedQueryId : (Guid?)null;
        var queryIdRange = default(GuidSearchRange);
        var hasQueryIdPrefix = queryId is null && GuidSearchRange.TryCreate(normalizedQuery, out queryIdRange);

        var p = new DynamicParameters();
        p.Add("perTypeLimit", perTypeLimit, dbType: DbType.Int32);
        p.Add("deletedStatus", (short)DocumentStatus.MarkedForDeletion, dbType: DbType.Int16);

        if (hasQuery)
        {
            p.Add("q", normalizedQuery, dbType: DbType.String);
            p.Add("queryId", queryId, dbType: DbType.Guid);
            p.Add("hasQueryIdPrefix", hasQueryIdPrefix, dbType: DbType.Boolean);
            p.Add("queryIdPrefixLower", hasQueryIdPrefix ? queryIdRange.Lower : Guid.Empty, dbType: DbType.Guid);
            p.Add("queryIdPrefixUpper", hasQueryIdPrefix ? queryIdRange.Upper : Guid.Empty, dbType: DbType.Guid);
        }

        var subqueries = new List<string>(distinctHeads.Length);

        for (var i = 0; i < distinctHeads.Length; i++)
        {
            var head = distinctHeads[i];
            var typeCodeParam = $"typeCode{i}";
            p.Add(typeCodeParam, head.TypeCode, dbType: DbType.String);

            var activeFilterSql = activeOnly ? "AND d.status <> @deletedStatus" : string.Empty;
            var headDisplaySql = $"h.{Qi(head.DisplayColumn)}";
            var labelSql = $"COALESCE({headDisplaySql}, d.id::text)";
            var fromSql = hasQuery
                ? $"documents d LEFT JOIN {Qi(head.HeadTableName)} h ON h.document_id = d.id"
                : $"{Qi(head.HeadTableName)} h JOIN documents d ON d.id = h.document_id";
            var searchFilterSql = hasQuery
                ? $"""
                  AND (
                      d.number ILIKE ('%' || @q::text || '%')
                      OR {headDisplaySql} ILIKE ('%' || @q::text || '%')
                      OR ({headDisplaySql} IS NULL AND (
                          (@queryId IS NOT NULL AND d.id = @queryId)
                          OR (@hasQueryIdPrefix AND d.id BETWEEN @queryIdPrefixLower AND @queryIdPrefixUpper)
                      ))
                  )
                  """
                : string.Empty;
            var orderBySql = hasQuery
                ? $"""
                  CASE
                      WHEN d.number IS NOT NULL AND d.number ILIKE ('%' || @q::text || '%') THEN 0
                      WHEN {headDisplaySql} ILIKE ('%' || @q::text || '%') THEN 1
                      WHEN {headDisplaySql} IS NULL AND @queryId IS NOT NULL AND d.id = @queryId THEN 2
                      WHEN {headDisplaySql} IS NULL AND @hasQueryIdPrefix AND d.id BETWEEN @queryIdPrefixLower AND @queryIdPrefixUpper THEN 2
                      ELSE 2
                  END,
                  {labelSql},
                  d.id
                  """
                : $"""
                  {headDisplaySql} NULLS LAST,
                  d.id
                  """;

            subqueries.Add($"""
                            (
                                SELECT
                                    d.id AS "Id",
                                    @{typeCodeParam} AS "TypeCode",
                                    d.status AS "Status",
                                    d.status = @deletedStatus AS "IsMarkedForDeletion",
                                    d.number AS "Number",
                                    {labelSql} AS "Label"
                                FROM {fromSql}
                                WHERE d.type_code = @{typeCodeParam}
                                  {activeFilterSql}
                                  {searchFilterSql}
                                ORDER BY
                                    {orderBySql}
                                LIMIT @perTypeLimit
                            )
                            """);
        }

        var sql = string.Join("\nUNION ALL\n", subqueries);

        var rows = await uow.Connection.QueryAsync<DocumentLookupSqlRow>(new CommandDefinition(
            sql,
            p,
            transaction: uow.Transaction,
            cancellationToken: ct));

        return rows
            .Select(row => new DocumentLookupRow(
                row.Id,
                row.TypeCode,
                row.Status,
                row.IsMarkedForDeletion,
                row.Label,
                row.Number))
            .ToList();
    }

    public async Task<IReadOnlyList<DocumentLookupRow>> GetByIdsAcrossTypesAsync(
        IReadOnlyList<DocumentHeadDescriptor> heads,
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default)
    {
        if (heads is null)
            throw new NgbArgumentRequiredException(nameof(heads));

        if (ids is null)
            throw new NgbArgumentRequiredException(nameof(ids));

        if (heads.Count == 0 || ids.Count == 0)
            return [];

        var distinctHeads = heads
            .Where(static head => head is not null)
            .GroupBy(head => head.TypeCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (distinctHeads.Length == 0)
            return [];

        var uniqIds = ids.Where(static id => id != Guid.Empty).Distinct().ToArray();
        if (uniqIds.Length == 0)
            return [];

        foreach (var head in distinctHeads)
        {
            EnsureValid(head);
        }

        await uow.EnsureConnectionOpenAsync(ct);

        var p = new DynamicParameters();
        p.Add("ids", uniqIds);
        p.Add("deletedStatus", (short)DocumentStatus.MarkedForDeletion, dbType: DbType.Int16);

        var subqueries = new List<string>(distinctHeads.Length);

        for (var i = 0; i < distinctHeads.Length; i++)
        {
            var head = distinctHeads[i];
            var typeCodeParam = $"typeCode{i}";
            p.Add(typeCodeParam, head.TypeCode, dbType: DbType.String);

            var displaySql = $"COALESCE(h.{Qi(head.DisplayColumn)}, d.id::text)";
            subqueries.Add($"""
                            (
                                SELECT
                                    d.id AS "Id",
                                    @{typeCodeParam} AS "TypeCode",
                                    d.status AS "Status",
                                    d.status = @deletedStatus AS "IsMarkedForDeletion",
                                    d.number AS "Number",
                                    {displaySql} AS "Label"
                                FROM documents d
                                LEFT JOIN {Qi(head.HeadTableName)} h ON h.document_id = d.id
                                WHERE d.type_code = @{typeCodeParam}
                                  AND d.id = ANY(@ids)
                            )
                            """);
        }

        var sql = string.Join("\nUNION ALL\n", subqueries);

        var rows = await uow.Connection.QueryAsync<DocumentLookupSqlRow>(new CommandDefinition(
            sql,
            p,
            transaction: uow.Transaction,
            cancellationToken: ct));

        return rows
            .Select(row => new DocumentLookupRow(
                row.Id,
                row.TypeCode,
                row.Status,
                row.IsMarkedForDeletion,
                row.Label,
                row.Number))
            .ToList();
    }

    private static void EnsureValid(DocumentHeadDescriptor head)
    {
        if (string.IsNullOrWhiteSpace(head.TypeCode))
            throw new NgbArgumentRequiredException(nameof(head.TypeCode));

        if (string.IsNullOrWhiteSpace(head.HeadTableName))
            throw new NgbArgumentRequiredException(nameof(head.HeadTableName));

        if (string.IsNullOrWhiteSpace(head.DisplayColumn))
            throw new NgbArgumentRequiredException(nameof(head.DisplayColumn));
    }

    private DocumentWhere BuildWhere(DocumentHeadDescriptor head, DocumentQuery query)
    {
        var p = new DynamicParameters();

        var clauses = new List<string>();
        var hasHeadCriteria = false;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // IMPORTANT: bind the parameter as text explicitly and cast in SQL to avoid 42P08.
            p.Add("search", query.Search.Trim(), dbType: DbType.String);
            clauses.Add($"{PostgresDocumentFilterSql.Qualify("h", head.DisplayColumn)} ILIKE ('%' || @search::text || '%')");
            hasHeadCriteria = true;
        }

        p.Add("deletedStatus", (short)DocumentStatus.MarkedForDeletion, dbType: DbType.Int16);

        switch (query.SoftDeleteFilterMode)
        {
            case SoftDeleteFilterMode.Active:
                clauses.Add("d.status <> @deletedStatus");
                break;
            case SoftDeleteFilterMode.Deleted:
                clauses.Add("d.status = @deletedStatus");
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
                clauses.Add(BuildFilterClause(head, f, $"f{i}", p));
                hasHeadCriteria = true;
                i++;
            }
        }

        if (query.PeriodFilter is not null)
        {
            if (query.PeriodFilter.FromInclusive is not null)
            {
                hasHeadCriteria = true;
                p.Add("periodFrom", query.PeriodFilter.FromInclusive.Value.ToDateTime(TimeOnly.MinValue), dbType: DbType.Date);
                clauses.Add($"{PostgresDocumentFilterSql.Qualify("h", query.PeriodFilter.ColumnName)} IS NOT NULL AND {PostgresDocumentFilterSql.Qualify("h", query.PeriodFilter.ColumnName)}::date >= @periodFrom");
            }

            if (query.PeriodFilter.ToInclusive is not null)
            {
                hasHeadCriteria = true;
                p.Add("periodTo", query.PeriodFilter.ToInclusive.Value.ToDateTime(TimeOnly.MinValue), dbType: DbType.Date);
                clauses.Add($"{PostgresDocumentFilterSql.Qualify("h", query.PeriodFilter.ColumnName)} IS NOT NULL AND {PostgresDocumentFilterSql.Qualify("h", query.PeriodFilter.ColumnName)}::date <= @periodTo");
            }
        }

        return new DocumentWhere(
            Sql: clauses.Count == 0 ? "TRUE" : string.Join(" AND ", clauses),
            Params: p,
            HasHeadCriteria: hasHeadCriteria);
    }

    private string BuildFilterClause(
        DocumentHeadDescriptor head,
        DocumentFilter filter,
        string parameterName,
        DynamicParameters parameters)
    {
        if (!string.IsNullOrWhiteSpace(filter.HeadColumnName))
            return PostgresDocumentFilterSql.BuildPredicate(
                PostgresDocumentFilterSql.Qualify("h", filter.HeadColumnName),
                filter,
                parameterName,
                parameters);

        foreach (var contributor in filterSqlContributors)
        {
            if (contributor.TryBuildClause(head, filter, "d", "h", parameterName, parameters, out var clause))
                return clause;
        }

        throw new NgbConfigurationViolationException(
            $"Document list filter '{filter.Key}' is not mapped to SQL for document '{head.TypeCode}'.",
            context: new Dictionary<string, object?>
            {
                ["documentType"] = head.TypeCode,
                ["filterKey"] = filter.Key
            });
    }

    private static string BuildSelectFields(DocumentHeadDescriptor head)
    {
        var cols = head.Columns
            .Where(c => !string.Equals(c.ColumnName, head.DisplayColumn, StringComparison.OrdinalIgnoreCase))
            .Select(c => $",\n       {PostgresDocumentFilterSql.Qualify("h", c.ColumnName)} AS \"{c.ColumnName}\"")
            .ToList();

        // Include the display column also as a field.
        cols.Insert(0, $",\n       {PostgresDocumentFilterSql.Qualify("h", head.DisplayColumn)} AS \"{head.DisplayColumn}\"");

        return string.Concat(cols);
    }

    private static string BuildNullSelectFields(DocumentHeadDescriptor head)
    {
        var cols = head.Columns
            .Where(c => !string.Equals(c.ColumnName, head.DisplayColumn, StringComparison.OrdinalIgnoreCase))
            .Select(c => $",\n       NULL AS \"{c.ColumnName}\"")
            .ToList();

        cols.Insert(0, $",\n       NULL AS \"{head.DisplayColumn}\"");

        return string.Concat(cols);
    }

    private sealed class DocumentLookupSqlRow
    {
        public Guid Id { get; init; }
        public string TypeCode { get; init; } = null!;
        public DocumentStatus Status { get; init; }
        public bool IsMarkedForDeletion { get; init; }
        public string? Label { get; init; }
        public string? Number { get; init; }
    }

    private sealed class DocumentHeadSqlRow
    {
        public Guid Id { get; init; }
        public string TypeCode { get; init; } = null!;
        public DocumentStatus Status { get; init; }
        public string? Display { get; init; }
        public string? Number { get; init; }
        public string? FieldsJson { get; init; }
    }

    private sealed record DocumentWhere(
        string Sql,
        DynamicParameters Params,
        bool HasHeadCriteria);

    private static DocumentHeadRow ToRow(DocumentHeadDescriptor head, IDictionary<string, object?> row)
    {
        var id = (Guid)row["Id"]!;
        var statusRaw = row["Status"]!;

        // Npgsql may return smallint as short or int depending on mapping.
        var status = statusRaw switch
        {
            short s => (DocumentStatus)s,
            int i => (DocumentStatus)(short)i,
            _ => throw new NgbInvariantViolationException("Unexpected document status DB type.",
                context: new Dictionary<string, object?> { ["type"] = statusRaw.GetType().FullName, ["value"] = statusRaw })
        };

        var isMarkedForDeletion = status == DocumentStatus.MarkedForDeletion;
        var display = row.TryGetValue("Display", out var d) ? d as string : null;
        var number = row.TryGetValue("Number", out var n) ? n as string : null;

        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in head.Columns)
        {
            row.TryGetValue(col.ColumnName, out var value);
            fields[col.ColumnName] = value;
        }

        return new DocumentHeadRow(id, status, isMarkedForDeletion, display, fields, number);
    }

    private static DocumentHeadRow ToHeadRow(DocumentHeadDescriptor head, DocumentHeadSqlRow row)
    {
        var fields = ParseFields(head, row.FieldsJson);
        var isMarkedForDeletion = row.Status == DocumentStatus.MarkedForDeletion;
        return new DocumentHeadRow(row.Id, row.Status, isMarkedForDeletion, row.Display, fields, row.Number);
    }

    private static IReadOnlyDictionary<string, object?> ParseFields(DocumentHeadDescriptor head, string? fieldsJson)
    {
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(fieldsJson))
        {
            foreach (var col in head.Columns)
            {
                fields[col.ColumnName] = null;
            }

            return fields;
        }

        using var doc = JsonDocument.Parse(fieldsJson);
        var root = doc.RootElement;

        foreach (var col in head.Columns)
        {
            if (root.TryGetProperty(col.ColumnName, out var value) && value.ValueKind != JsonValueKind.Null)
            {
                fields[col.ColumnName] = ConvertJsonValue(value, col.ColumnType);
                continue;
            }

            fields[col.ColumnName] = null;
        }

        return fields;
    }

    private static object? ConvertJsonValue(JsonElement value, ColumnType columnType)
        => columnType switch
        {
            ColumnType.String => value.GetString(),
            ColumnType.Int32 => value.GetInt32(),
            ColumnType.Int64 => value.GetInt64(),
            ColumnType.Decimal => value.GetDecimal(),
            ColumnType.Boolean => value.GetBoolean(),
            ColumnType.Guid => Guid.Parse(value.GetString()!),
            ColumnType.Date => DateOnly.Parse(value.GetString()!, CultureInfo.InvariantCulture),
            ColumnType.DateTimeUtc => ParseUtc(value),
            _ => value.GetRawText()
        };

    private static DateTime ParseUtc(JsonElement value)
    {
        return DateTime.Parse(
            value.GetString()!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static string Qi(string ident) => PostgresDocumentFilterSql.QuoteIdentifier(ident);

    private static string BuildFieldsJson(DocumentHeadDescriptor head)
    {
        if (head.Columns.Count == 0)
            return "'{}'::jsonb";

        var args = head.Columns
            .SelectMany(c => new[]
            {
                Qs(c.ColumnName),
                PostgresDocumentFilterSql.Qualify("h", c.ColumnName)
            });

        return $"jsonb_build_object({string.Join(", ", args)})";
    }

    private static string Qs(string literal) => "'" + literal.Replace("'", "''", StringComparison.Ordinal) + "'";
}
