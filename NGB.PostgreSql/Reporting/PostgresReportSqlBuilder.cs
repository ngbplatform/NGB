using System.Text.Json;
using Dapper;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.PostgreSql.Internal;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Reporting;

public sealed class PostgresReportSqlBuilder(PostgresReportDatasetCatalog datasets)
{
    internal const int MaxCursorlessOffset = 10_000;
    private const string DisplayFieldSuffix = "_display";
    private const string IdFieldSuffix = "_id";

    private readonly PostgresReportDatasetCatalog _datasets = datasets
        ?? throw new NgbConfigurationViolationException("PostgreSQL reporting SQL builder requires a dataset catalog registration.");

    public PostgresReportSqlStatement Build(PostgresReportExecutionRequest request)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        ValidateCursorlessOffset(request.Paging);

        var dataset = _datasets.GetDataset(request.DatasetCodeNorm);
        var selectSql = new List<string>();
        var groupBySql = new List<string>();
        var orderColumns = new List<PostgresReportCursorColumn>();
        var whereSql = new List<string>();
        var parameters = new DynamicParameters();
        var columns = new List<PostgresReportOutputColumn>();
        var predicateIndex = 0;
        var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rowGroup in request.RowGroups)
        {
            var fieldBinding = dataset.GetField(rowGroup.FieldCode);
            var expression = fieldBinding.ResolveExpression(rowGroup.TimeGrain);
            var alias = EnsureSafeAlias(rowGroup.OutputCode, $"row-group:{rowGroup.FieldCode}");
            AddProjectedColumn(selectSql, groupBySql, columns, usedAliases, expression, alias, rowGroup.Label, rowGroup.DataType, "row-group", includeInGroupBy: true);
        }

        foreach (var columnGroup in request.ColumnGroups)
        {
            var fieldBinding = dataset.GetField(columnGroup.FieldCode);
            var expression = fieldBinding.ResolveExpression(columnGroup.TimeGrain);
            var alias = EnsureSafeAlias(columnGroup.OutputCode, $"column-group:{columnGroup.FieldCode}");
            AddProjectedColumn(selectSql, groupBySql, columns, usedAliases, expression, alias, columnGroup.Label, columnGroup.DataType, "column-group", includeInGroupBy: true);
        }

        foreach (var detailField in request.DetailFields)
        {
            var fieldBinding = dataset.GetField(detailField.FieldCode);
            var expression = fieldBinding.ResolveExpression(null);
            var alias = EnsureSafeAlias(detailField.OutputCode, $"detail:{detailField.FieldCode}");
            AddProjectedColumn(selectSql, groupBySql, columns, usedAliases, expression, alias, detailField.Label, detailField.DataType, "detail", includeInGroupBy: true);
        }

        foreach (var measure in request.Measures)
        {
            var measureBinding = dataset.GetMeasure(measure.MeasureCode);
            var alias = EnsureSafeAlias(measure.OutputCode, $"measure:{measure.MeasureCode}");
            var expression = measureBinding.ResolveAggregateExpression(measure.Aggregation);
            AddProjectedColumn(selectSql, groupBySql, columns, usedAliases, expression, alias, measure.Label, measure.DataType, "measure", includeInGroupBy: false);
        }

        AppendInteractiveSupportFields(request, dataset, selectSql, groupBySql, columns, usedAliases);

        if (selectSql.Count == 0)
            throw new NgbConfigurationViolationException($"PostgreSQL reporting request for dataset '{dataset.DatasetCodeNorm}' must select at least one row group, column group, detail field, or measure.");

        if (!string.IsNullOrWhiteSpace(dataset.BaseWhereSql))
            whereSql.Add($"({dataset.BaseWhereSql})");

        foreach (var pair in request.Parameters)
        {
            parameters.Add(pair.Key, pair.Value);
        }

        foreach (var predicate in request.Predicates)
        {
            var fieldBinding = dataset.GetField(predicate.FieldCode);
            var expression = fieldBinding.ResolveExpression(null);
            var parameterName = $"p_{predicateIndex++}";
            whereSql.Add(BuildPredicateSql(expression, parameterName, predicate.Filter, parameters));
        }

        foreach (var sort in request.Sorts)
        {
            var sortAlias = ResolveSortAlias(request, sort);
            AddOrderColumn(orderColumns, columns, sortAlias, sort.Direction);
        }

        if (orderColumns.Count == 0)
        {
            if (request.RowGroups.Count > 0)
            {
                foreach (var rowGroup in request.RowGroups)
                {
                    AddOrderColumn(orderColumns, columns, EnsureSafeAlias(rowGroup.OutputCode, $"order-row-group:{rowGroup.FieldCode}"), ReportSortDirection.Asc);
                }
            }

            if (request.ColumnGroups.Count > 0)
            {
                foreach (var columnGroup in request.ColumnGroups)
                {
                    AddOrderColumn(orderColumns, columns, EnsureSafeAlias(columnGroup.OutputCode, $"order-column-group:{columnGroup.FieldCode}"), ReportSortDirection.Asc);
                }
            }

            if (orderColumns.Count == 0 && request.DetailFields.Count > 0)
            {
                foreach (var detail in request.DetailFields)
                {
                    AddOrderColumn(orderColumns, columns, EnsureSafeAlias(detail.OutputCode, $"order-detail:{detail.FieldCode}"), ReportSortDirection.Asc);
                }
            }

            if (orderColumns.Count == 0)
            {
                foreach (var measure in request.Measures)
                {
                    AddOrderColumn(orderColumns, columns, EnsureSafeAlias(measure.OutputCode, $"order-measure:{measure.MeasureCode}"), ReportSortDirection.Asc);
                }
            }
        }

        var cursorColumns = BuildCursorColumns(
            request,
            dataset,
            selectSql,
            columns,
            usedAliases,
            orderColumns);

        if (!request.Paging.DisablePaging && !string.IsNullOrWhiteSpace(request.Paging.Cursor) && cursorColumns.Count == 0)
        {
            throw new NgbArgumentInvalidException(
                "cursor",
                "This composable dataset does not define a stable keyset cursor. Omit cursor paging or configure stable cursor key fields for the dataset.");
        }

        var cursorValues = !request.Paging.DisablePaging && !string.IsNullOrWhiteSpace(request.Paging.Cursor)
            ? PostgresReportCursorCodec.Decode(request.Paging.Cursor, dataset.DatasetCodeNorm, cursorColumns)
            : null;

        if (request.Paging.DisablePaging)
        {
            parameters.Add("materialization_limit_plus_one", PagingLimits.MaxMaterializedRows + 1);
        }
        else
        {
            parameters.Add("limit_plus_one", request.Paging.Limit + 1);
            if (cursorValues is null && request.Paging.Offset > 0)
                parameters.Add("offset", PagingLimits.BoundOffset(request.Paging.Offset));
        }

        var innerSql = $"""
SELECT
    {string.Join(",", selectSql)}
FROM {dataset.FromSql}
{BuildWhereClause(whereSql)}
{BuildGroupByClause(groupBySql, request.Measures.Count > 0)}
""";

        var orderBySql = BuildOrderBySql(orderColumns, cursorColumns.Count > 0);
        string sql;
        if (cursorValues is not null)
        {
            var cursorPredicate = BuildCursorPredicate(cursorColumns, cursorValues, parameters);
            sql = $"""
SELECT *
FROM (
{Indent(innerSql, 4)}
) report_rows
WHERE {cursorPredicate}
ORDER BY {orderBySql}
LIMIT @limit_plus_one;
""";
        }
        else
        {
            var pagingSql = request.Paging.DisablePaging
                ? "LIMIT @materialization_limit_plus_one"
                : request.Paging.Offset > 0
                    ? "OFFSET @offset\nLIMIT @limit_plus_one"
                    : "LIMIT @limit_plus_one";
            sql = $"""
{innerSql}
ORDER BY {orderBySql}
{pagingSql};
""";
        }

        return new PostgresReportSqlStatement(
            Sql: sql,
            Parameters: parameters,
            Columns: columns,
            IsAggregated: request.Measures.Count > 0,
            Offset: request.Paging.DisablePaging || cursorValues is not null ? 0 : PagingLimits.BoundOffset(request.Paging.Offset),
            Limit: request.Paging.DisablePaging ? 0 : request.Paging.Limit,
            DatasetCode: dataset.DatasetCodeNorm,
            CursorColumns: cursorColumns);
    }

    private static void ValidateCursorlessOffset(PostgresReportPaging paging)
    {
        if (paging.DisablePaging)
            return;

        if (string.IsNullOrWhiteSpace(paging.Cursor) && paging.Offset > MaxCursorlessOffset)
        {
            throw new NgbArgumentOutOfRangeException(
                "offset",
                paging.Offset,
                $"Composable report offset must be between 0 and {MaxCursorlessOffset}. Narrow the filters or use a canonical cursor-enabled report for deeper navigation.");
        }
    }

    private static IReadOnlyList<PostgresReportCursorColumn> BuildCursorColumns(
        PostgresReportExecutionRequest request,
        PostgresReportDatasetBinding dataset,
        ICollection<string> selectSql,
        IReadOnlyList<PostgresReportOutputColumn> columns,
        ISet<string> usedAliases,
        List<PostgresReportCursorColumn> orderColumns)
    {
        if (request.Paging.DisablePaging)
            return [];

        if (request.Measures.Count > 0)
        {
            var groupingColumns = columns.Where(x => !string.Equals(x.SemanticRole, "measure", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (groupingColumns.Length == 0)
                return [];

            foreach (var column in groupingColumns)
            {
                AddOrderColumn(orderColumns, columns, column.OutputCode, ReportSortDirection.Asc);
            }

            return orderColumns;
        }

        if (dataset.CursorKeyFields.Count == 0)
            return [];

        for (var i = 0; i < dataset.CursorKeyFields.Count; i++)
        {
            var keyField = dataset.CursorKeyFields[i];
            var visibleAlias = ResolveSelectedRawFieldAlias(request, columns, keyField.FieldCodeNorm);
            if (visibleAlias is not null)
            {
                AddOrderColumn(orderColumns, columns, visibleAlias, ReportSortDirection.Asc);
                continue;
            }

            var hiddenAlias = EnsureSafeAlias($"__cursor_key_{i}", $"cursor-key:{keyField.FieldCodeNorm}");
            if (!usedAliases.Add(hiddenAlias))
                throw new NgbInvariantViolationException($"PostgreSQL reporting duplicate cursor alias '{hiddenAlias}'.");

            selectSql.Add($"{keyField.ResolveExpression(null)} AS {hiddenAlias}");
            if (orderColumns.All(x => !x.Alias.Equals(hiddenAlias, StringComparison.OrdinalIgnoreCase)))
                orderColumns.Add(new PostgresReportCursorColumn(hiddenAlias, keyField.DataType, ReportSortDirection.Asc, IsHidden: true));
        }

        return orderColumns;
    }

    private static string? ResolveSelectedRawFieldAlias(
        PostgresReportExecutionRequest request,
        IReadOnlyList<PostgresReportOutputColumn> columns,
        string fieldCode)
    {
        var grouping = request.RowGroups
            .Concat(request.ColumnGroups)
            .FirstOrDefault(x => x.TimeGrain is null && x.FieldCode.Equals(fieldCode, StringComparison.OrdinalIgnoreCase));

        if (grouping is not null)
            return grouping.OutputCode;

        var detail = request.DetailFields.FirstOrDefault(x => x.FieldCode.Equals(fieldCode, StringComparison.OrdinalIgnoreCase));
        if (detail is not null)
            return detail.OutputCode;

        return columns.Any(x => x.OutputCode.Equals(fieldCode, StringComparison.OrdinalIgnoreCase))
            ? fieldCode
            : null;
    }

    private static void AddOrderColumn(
        ICollection<PostgresReportCursorColumn> orderColumns,
        IReadOnlyList<PostgresReportOutputColumn> columns,
        string alias,
        ReportSortDirection direction)
    {
        if (orderColumns.Any(x => x.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)))
            return;

        var output = columns.FirstOrDefault(x => x.OutputCode.Equals(alias, StringComparison.OrdinalIgnoreCase))
            ?? throw new NgbInvariantViolationException($"PostgreSQL reporting order alias '{alias}' is not projected.");
        orderColumns.Add(new PostgresReportCursorColumn(alias, output.DataType, direction, IsHidden: false));
    }

    private static string BuildOrderBySql(
        IReadOnlyList<PostgresReportCursorColumn> orderColumns,
        bool deterministicNullOrder)
        => string.Join(", ", orderColumns.Select(x =>
            $"{x.Alias} {(x.Direction == ReportSortDirection.Desc ? "DESC" : "ASC")}{(deterministicNullOrder ? " NULLS LAST" : string.Empty)}"));

    private static string BuildCursorPredicate(
        IReadOnlyList<PostgresReportCursorColumn> columns,
        IReadOnlyList<object?> values,
        DynamicParameters parameters)
    {
        var terms = new List<string>(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            var parameterName = $"cursor_{i}";
            parameters.Add(parameterName, values[i]);
            var prefix = string.Join(" AND ", columns.Take(i).Select((x, index) => $"{x.Alias} IS NOT DISTINCT FROM @cursor_{index}"));
            var comparison = columns[i].Direction == ReportSortDirection.Desc ? "<" : ">";
            var current = $"@{parameterName} IS NOT NULL AND ({columns[i].Alias} IS NULL OR {columns[i].Alias} {comparison} @{parameterName})";
            terms.Add(i == 0 ? $"({current})" : $"({prefix} AND {current})");
        }

        return $"({string.Join(" OR ", terms)})";
    }

    private static string Indent(string value, int spaces)
    {
        var prefix = new string(' ', spaces);
        return string.Join(Environment.NewLine, value.TrimEnd().Split('\n').Select(line => prefix + line.TrimEnd('\r')));
    }

    private static void AppendInteractiveSupportFields(
        PostgresReportExecutionRequest request,
        PostgresReportDatasetBinding dataset,
        ICollection<string> selectSql,
        ICollection<string> groupBySql,
        ICollection<PostgresReportOutputColumn> columns,
        ISet<string> usedAliases)
    {
        if (ShouldIncludeSupportField(request, "account_display") && dataset.Fields.TryGetValue("account_id", out var accountIdField))
        {
            AppendSupportField(
                selectSql,
                groupBySql,
                columns,
                usedAliases,
                accountIdField.ResolveExpression(null),
                ReportInteractiveSupport.SupportAccountId,
                "uuid");
        }

        if (ShouldIncludeSupportField(request, "document_display") && dataset.Fields.TryGetValue("document_id", out var documentIdField))
        {
            AppendSupportField(
                selectSql,
                groupBySql,
                columns,
                usedAliases,
                documentIdField.ResolveExpression(null),
                ReportInteractiveSupport.SupportDocumentId, 
                "uuid");
        }

        foreach (var supportFieldCode in ResolveCatalogSupportFieldCodes(request, dataset))
        {
            var supportField = dataset.GetField(supportFieldCode);
            AppendSupportField(
                selectSql,
                groupBySql,
                columns,
                usedAliases,
                supportField.ResolveExpression(null),
                supportFieldCode,
                supportField.DataType);
        }
    }

    private static bool ShouldIncludeSupportField(PostgresReportExecutionRequest request, string fieldCode)
        => request.RowGroups.Any(x => x.FieldCode.Equals(fieldCode, StringComparison.OrdinalIgnoreCase))
           || request.ColumnGroups.Any(x => x.FieldCode.Equals(fieldCode, StringComparison.OrdinalIgnoreCase))
           || request.DetailFields.Any(x => x.FieldCode.Equals(fieldCode, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> ResolveCatalogSupportFieldCodes(
        PostgresReportExecutionRequest request,
        PostgresReportDatasetBinding dataset)
    {
        var supportFieldCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fieldCode in request.RowGroups.Select(x => x.FieldCode)
                     .Concat(request.ColumnGroups.Select(x => x.FieldCode))
                     .Concat(request.DetailFields.Select(x => x.FieldCode)))
        {
            if (!fieldCode.EndsWith(DisplayFieldSuffix, StringComparison.OrdinalIgnoreCase)
                || fieldCode.Equals("account_display", StringComparison.OrdinalIgnoreCase)
                || fieldCode.Equals("document_display", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var supportFieldCode = string.Concat(fieldCode.AsSpan(0, fieldCode.Length - DisplayFieldSuffix.Length), IdFieldSuffix);
            if (!dataset.Fields.ContainsKey(supportFieldCode) || IsFieldSelected(request, supportFieldCode))
                continue;

            supportFieldCodes.Add(supportFieldCode);
        }

        return supportFieldCodes.ToArray();
    }

    private static bool IsFieldSelected(PostgresReportExecutionRequest request, string fieldCode)
        => request.RowGroups.Any(x => x.FieldCode.Equals(fieldCode, StringComparison.OrdinalIgnoreCase))
           || request.ColumnGroups.Any(x => x.FieldCode.Equals(fieldCode, StringComparison.OrdinalIgnoreCase))
           || request.DetailFields.Any(x => x.FieldCode.Equals(fieldCode, StringComparison.OrdinalIgnoreCase));

    private static void AppendSupportField(
        ICollection<string> selectSql,
        ICollection<string> groupBySql,
        ICollection<PostgresReportOutputColumn> columns,
        ISet<string> usedAliases,
        string expression,
        string alias,
        string dataType)
    {
        var safeAlias = EnsureSafeAlias(alias, $"support:{alias}");
        AddProjectedColumn(selectSql, groupBySql, columns, usedAliases, expression, safeAlias, safeAlias, dataType, "support", includeInGroupBy: true);
    }

    private static void AddProjectedColumn(
        ICollection<string> selectSql,
        ICollection<string> groupBySql,
        ICollection<PostgresReportOutputColumn> columns,
        ISet<string> usedAliases,
        string expression,
        string alias,
        string title,
        string dataType,
        string semanticRole,
        bool includeInGroupBy)
    {
        if (!usedAliases.Add(alias))
            throw new NgbInvariantViolationException($"PostgreSQL reporting duplicate projected alias '{alias}'. Validation should have prevented this state.");

        selectSql.Add($"{expression} AS {alias}");

        if (includeInGroupBy)
            groupBySql.Add(expression);

        columns.Add(new PostgresReportOutputColumn(alias, title, dataType, semanticRole));
    }

    private static string BuildPredicateSql(
        string expression,
        string parameterName,
        ReportFilterValueDto filter,
        DynamicParameters parameters)
    {
        var value = filter.Value;
        if (value.ValueKind == JsonValueKind.Null)
            return $"{expression} IS NULL";

        if (value.ValueKind == JsonValueKind.Array)
        {
            parameters.Add(parameterName, ConvertJsonArray(value));
            return $"{expression} = ANY(@{parameterName})";
        }

        parameters.Add(parameterName, ConvertJsonElement(value));
        return $"{expression} = @{parameterName}";
    }

    private static Array ConvertJsonArray(JsonElement value)
    {
        var items = value.EnumerateArray().Select(ConvertJsonElement).ToList();
        if (items.Count == 0)
            return Array.Empty<string>();

        if (items.All(x => x is Guid))
            return items.Cast<Guid>().ToArray();

        if (items.All(x => x is string or null))
            return items.Cast<string?>().ToArray();

        if (items.All(x => x is long))
            return items.Cast<long>().ToArray();

        if (items.All(x => x is decimal))
            return items.Cast<decimal>().ToArray();

        if (items.All(x => x is double))
            return items.Cast<double>().ToArray();

        return items.ToArray();
    }

    private static object? ConvertJsonElement(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String when value.TryGetGuid(out var guid) => guid,
            JsonValueKind.String when value.TryGetDateTimeOffset(out var dto) => dto,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out var i64) => i64,
            JsonValueKind.Number when value.TryGetDecimal(out var dec) => dec,
            JsonValueKind.Number when value.TryGetDouble(out var dbl) => dbl,
            _ => value.GetRawText()
        };

    private static string ResolveSortAlias(PostgresReportExecutionRequest request, PostgresReportSortSelection sort)
    {
        if (!string.IsNullOrWhiteSpace(sort.MeasureCode))
        {
            var measure = request.Measures
                .FirstOrDefault(x => x.MeasureCode.Equals(sort.MeasureCode, StringComparison.OrdinalIgnoreCase));
            if (measure is null)
                throw new NgbConfigurationViolationException($"PostgreSQL reporting sort measure '{sort.MeasureCode}' is not selected.");

            return EnsureSafeAlias(measure.OutputCode, $"sort-measure:{measure.MeasureCode}");
        }

        var groups = sort.AppliesToColumnAxis ? request.ColumnGroups : request.RowGroups;
        var grouped = !string.IsNullOrWhiteSpace(sort.GroupKey)
            ? groups.FirstOrDefault(x => string.Equals(x.GroupKey, sort.GroupKey, StringComparison.OrdinalIgnoreCase))
            : groups.FirstOrDefault(x => x.FieldCode.Equals(sort.FieldCode, StringComparison.OrdinalIgnoreCase) && x.TimeGrain == sort.TimeGrain);
        if (grouped is not null)
            return EnsureSafeAlias(grouped.OutputCode, $"sort-group:{grouped.GroupKey ?? grouped.FieldCode}");

        if (!sort.AppliesToColumnAxis)
        {
            var detail = request.DetailFields
                .FirstOrDefault(x => x.FieldCode.Equals(sort.FieldCode, StringComparison.OrdinalIgnoreCase));
            if (detail is not null)
                return EnsureSafeAlias(detail.OutputCode, $"sort-detail:{detail.FieldCode}");
        }

        throw new NgbConfigurationViolationException($"PostgreSQL reporting sort field '{sort.FieldCode}' is not selected.");
    }

    private static string BuildWhereClause(IReadOnlyList<string> predicates)
        => predicates.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", predicates)}";

    private static string BuildGroupByClause(IReadOnlyList<string> expressions, bool isAggregated)
        => !isAggregated || expressions.Count == 0
            ? string.Empty
            : $"GROUP BY {string.Join(",", expressions)}";

    private static string EnsureSafeAlias(string alias, string context)
    {
        PostgresSqlIdentifiers.EnsureOrThrow(alias, context);
        return alias;
    }
}
