using System.Text.Json;
using Dapper;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.PostgreSql.Internal;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Reporting;

public sealed class PostgresReportSqlBuilder(PostgresReportDatasetCatalog datasets)
{
    private const string DisplayFieldSuffix = "_display";
    private const string IdFieldSuffix = "_id";

    private readonly PostgresReportDatasetCatalog _datasets = datasets
        ?? throw new NgbConfigurationViolationException("PostgreSQL reporting SQL builder requires a dataset catalog registration.");

    public PostgresReportSqlStatement Build(PostgresReportExecutionRequest request)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var dataset = _datasets.GetDataset(request.DatasetCodeNorm);
        var selectSql = new List<string>();
        var groupBySql = new List<string>();
        var orderBySql = new List<string>();
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
            var directionSql = sort.Direction == ReportSortDirection.Desc ? "DESC" : "ASC";
            orderBySql.Add($"{sortAlias} {directionSql}");
        }

        if (orderBySql.Count == 0)
        {
            if (request.RowGroups.Count > 0)
                orderBySql.AddRange(request.RowGroups.Select(x => EnsureSafeAlias(x.OutputCode, $"order-row-group:{x.FieldCode}")));

            if (request.ColumnGroups.Count > 0)
                orderBySql.AddRange(request.ColumnGroups.Select(x => EnsureSafeAlias(x.OutputCode, $"order-column-group:{x.FieldCode}")));

            if (orderBySql.Count == 0 && request.DetailFields.Count > 0)
                orderBySql.AddRange(request.DetailFields.Select(x => EnsureSafeAlias(x.OutputCode, $"order-detail:{x.FieldCode}")));

            if (orderBySql.Count == 0)
                orderBySql.AddRange(request.Measures.Select(x => EnsureSafeAlias(x.OutputCode, $"order-measure:{x.MeasureCode}")));
        }

        if (!request.Paging.DisablePaging)
        {
            parameters.Add("offset", request.Paging.Offset);
            parameters.Add("limit_plus_one", request.Paging.Limit + 1);
        }

        var pagingSql = request.Paging.DisablePaging
            ? string.Empty
            : """
OFFSET @offset
LIMIT @limit_plus_one
""";

        var sql = $"""
SELECT
    {string.Join(",", selectSql)}
FROM {dataset.FromSql}
{BuildWhereClause(whereSql)}
{BuildGroupByClause(groupBySql, request.Measures.Count > 0)}
ORDER BY {string.Join(", ", orderBySql)}
{pagingSql};
""";

        return new PostgresReportSqlStatement(
            Sql: sql,
            Parameters: parameters,
            Columns: columns,
            IsAggregated: request.Measures.Count > 0,
            Offset: request.Paging.DisablePaging ? 0 : request.Paging.Offset,
            Limit: request.Paging.DisablePaging ? 0 : request.Paging.Limit);
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
