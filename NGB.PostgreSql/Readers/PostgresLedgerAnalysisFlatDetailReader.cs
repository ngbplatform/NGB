using System.Text.Json;
using Dapper;
using NGB.Accounting.Reports.LedgerAnalysis;
using NGB.Application.Abstractions.Services;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.Reporting;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresLedgerAnalysisFlatDetailReader(IUnitOfWork uow, PostgresReportDatasetCatalog datasets)
    : ILedgerAnalysisFlatDetailReader
{
    private const string CursorPeriodAlias = "__cursor_period_utc";
    private const string CursorEntryAlias = "__cursor_entry_id";
    private const string CursorPostingSideAlias = "__cursor_posting_side";

    private readonly IUnitOfWork _uow = uow ?? throw new NgbConfigurationViolationException("Ledger analysis flat detail reader requires a unit of work registration.");
    private readonly PostgresReportDatasetCatalog _datasets = datasets ?? throw new NgbConfigurationViolationException("Ledger analysis flat detail reader requires a dataset catalog registration.");

    public async Task<LedgerAnalysisFlatDetailPage> GetPageAsync(
        LedgerAnalysisFlatDetailPageRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (request is { DisablePaging: false, PageSize: <= 0 })
            throw new NgbArgumentInvalidException(nameof(request.PageSize), "Page size must be positive.");

        var dataset = _datasets.GetDataset(request.DatasetCode);
        var parameters = new DynamicParameters();
        parameters.Add("from_utc", request.FromUtc);
        parameters.Add("to_utc_exclusive", request.ToUtcExclusive);
        if (!request.DisablePaging)
            parameters.Add("limit_plus_one", request.PageSize + 1);

        var selectSql = new List<string>
        {
            $"{dataset.GetField("period_utc").ResolveExpression(null)} AS {CursorPeriodAlias}",
            $"{dataset.GetField("entry_id").ResolveExpression(null)} AS {CursorEntryAlias}",
            $"{dataset.GetField("posting_side").ResolveExpression(null)} AS {CursorPostingSideAlias}"
        };
        var whereSql = new List<string>();
        var predicateIndex = 0;
        var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CursorPeriodAlias,
            CursorEntryAlias,
            CursorPostingSideAlias
        };

        foreach (var detailField in request.DetailFields)
        {
            var binding = dataset.GetField(detailField.FieldCode);
            AddProjectedColumn(selectSql, usedAliases, binding.ResolveExpression(null), detailField.OutputCode, $"detail:{detailField.FieldCode}");
        }

        foreach (var measure in request.Measures)
        {
            var binding = dataset.GetMeasure(measure.MeasureCode);
            AddProjectedColumn(selectSql, usedAliases, binding.SqlExpression, measure.OutputCode, $"measure:{measure.MeasureCode}");
        }

        AppendInteractiveSupportFields(request, dataset, selectSql, usedAliases);

        if (!string.IsNullOrWhiteSpace(dataset.BaseWhereSql))
            whereSql.Add($"({dataset.BaseWhereSql})");

        foreach (var predicate in request.Predicates)
        {
            var fieldBinding = dataset.GetField(predicate.FieldCode);
            var expression = fieldBinding.ResolveExpression(null);
            var parameterName = $"p_{predicateIndex++}";
            whereSql.Add(BuildPredicateSql(expression, parameterName, predicate.Value, parameters));
        }

        if (!request.DisablePaging && request.Cursor is not null)
        {
            parameters.Add("after_period_utc", request.Cursor.AfterPeriodUtc);
            parameters.Add("after_entry_id", request.Cursor.AfterEntryId);
            parameters.Add("after_posting_side", request.Cursor.AfterPostingSide);

            var periodExpression = dataset.GetField("period_utc").ResolveExpression(null);
            var entryExpression = dataset.GetField("entry_id").ResolveExpression(null);
            var postingSideExpression = dataset.GetField("posting_side").ResolveExpression(null);

            whereSql.Add(
                $"(({periodExpression} > @after_period_utc) OR ({periodExpression} = @after_period_utc AND {entryExpression} > @after_entry_id) OR ({periodExpression} = @after_period_utc AND {entryExpression} = @after_entry_id AND {postingSideExpression} > @after_posting_side))");
        }

        var sql = $"""
SELECT
    {string.Join(",\n    ", selectSql)}
FROM {dataset.FromSql}
{BuildWhereClause(whereSql)}
ORDER BY {CursorPeriodAlias}, {CursorEntryAlias}, {CursorPostingSideAlias}
{(request.DisablePaging ? string.Empty : "LIMIT @limit_plus_one;")}
""";

        await _uow.EnsureConnectionOpenAsync(ct);

        var rawRows = (await _uow.Connection.QueryAsync(
                new CommandDefinition(
                    sql,
                    parameters,
                    _uow.Transaction,
                    cancellationToken: ct)))
            .Select(MaterializeRow)
            .ToList();

        var hasMore = !request.DisablePaging && rawRows.Count > request.PageSize;
        if (hasMore)
            rawRows.RemoveAt(rawRows.Count - 1);

        var nextCursor = BuildNextCursor(rawRows, hasMore);

        var rows = rawRows
            .Select(RemoveHiddenCursorValues)
            .Select(static x => new LedgerAnalysisFlatDetailRow(x))
            .ToList();

        return new LedgerAnalysisFlatDetailPage(rows, hasMore, nextCursor);
    }

    private static void AppendInteractiveSupportFields(
        LedgerAnalysisFlatDetailPageRequest request,
        PostgresReportDatasetBinding dataset,
        ICollection<string> selectSql,
        ISet<string> usedAliases)
    {
        if (request.DetailFields.Any(x => x.FieldCode.Equals("account_display", StringComparison.OrdinalIgnoreCase))
            && dataset.Fields.TryGetValue("account_id", out var accountIdField))
        {
            AddProjectedColumn(
                selectSql,
                usedAliases,
                accountIdField.ResolveExpression(null),
                ReportInteractiveSupport.SupportAccountId,
                "support:account");
        }

        if (request.DetailFields.Any(x => x.FieldCode.Equals("document_display", StringComparison.OrdinalIgnoreCase))
            && dataset.Fields.TryGetValue("document_id", out var documentIdField))
        {
            AddProjectedColumn(
                selectSql,
                usedAliases,
                documentIdField.ResolveExpression(null),
                ReportInteractiveSupport.SupportDocumentId,
                "support:document");
        }
    }

    private static void AddProjectedColumn(
        ICollection<string> selectSql,
        ISet<string> usedAliases,
        string expression,
        string alias,
        string context)
    {
        var safeAlias = EnsureSafeAlias(alias, context);
        if (!usedAliases.Add(safeAlias))
            throw new NgbInvariantViolationException($"Ledger analysis flat detail reader duplicate projected alias '{safeAlias}'.");

        selectSql.Add($"{expression} AS {safeAlias}");
    }

    private static string BuildPredicateSql(
        string expression,
        string parameterName,
        JsonElement value,
        DynamicParameters parameters)
    {
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

    private static string BuildWhereClause(IReadOnlyList<string> predicates)
        => predicates.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", predicates)}";

    private static LedgerAnalysisFlatDetailCursor? BuildNextCursor(
        IReadOnlyList<Dictionary<string, object?>> rows,
        bool hasMore)
    {
        if (!hasMore || rows.Count == 0)
            return null;

        var last = rows[^1];
        var afterPeriodUtc = ReadDateTimeUtc(last, CursorPeriodAlias);
        var afterEntryId = ReadInt64(last, CursorEntryAlias);
        var afterPostingSide = ReadString(last, CursorPostingSideAlias);
        return new LedgerAnalysisFlatDetailCursor(afterPeriodUtc, afterEntryId, afterPostingSide);
    }

    private static Dictionary<string, object?> RemoveHiddenCursorValues(Dictionary<string, object?> row)
    {
        var materialized = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase);
        materialized.Remove(CursorPeriodAlias);
        materialized.Remove(CursorEntryAlias);
        materialized.Remove(CursorPostingSideAlias);
        return materialized;
    }

    private static DateTime ReadDateTimeUtc(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var raw) || raw is null)
            throw new NgbInvariantViolationException($"Ledger analysis flat detail cursor requires value '{key}'.");

        return raw switch
        {
            DateTime dateTime => dateTime.Kind == DateTimeKind.Utc ? dateTime : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            DateTimeOffset dto => dto.UtcDateTime,
            string text when DateTime.TryParse(text, out var parsed) => DateTime.SpecifyKind(parsed, DateTimeKind.Utc),
            _ => throw new NgbInvariantViolationException($"Ledger analysis flat detail cursor value '{key}' must be a timestamp.")
        };
    }

    private static long ReadInt64(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var raw) || raw is null)
            throw new NgbInvariantViolationException($"Ledger analysis flat detail cursor requires value '{key}'.");

        return raw switch
        {
            long i64 => i64,
            int i32 => i32,
            decimal dec => (long)dec,
            string text when long.TryParse(text, out var parsed) => parsed,
            _ => throw new NgbInvariantViolationException($"Ledger analysis flat detail cursor value '{key}' must be an integer.")
        };
    }

    private static string ReadString(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var raw) || raw is null)
            throw new NgbInvariantViolationException($"Ledger analysis flat detail cursor requires value '{key}'.");

        return raw switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text,
            _ => throw new NgbInvariantViolationException($"Ledger analysis flat detail cursor value '{key}' must be a non-empty string.")
        };
    }

    private static Dictionary<string, object?> MaterializeRow(dynamic row)
    {
        if (row is IDictionary<string, object?> typed)
            return new Dictionary<string, object?>(typed, StringComparer.OrdinalIgnoreCase);

        if (row is IDictionary<string, object> boxed)
            return boxed.ToDictionary(x => x.Key, object? (x) => x.Value, StringComparer.OrdinalIgnoreCase);

        throw new NgbInvariantViolationException("Ledger analysis flat detail reader expected Dapper row materialization to provide a dictionary payload.");
    }

    private static string EnsureSafeAlias(string alias, string context)
    {
        PostgresSqlIdentifiers.EnsureOrThrow(alias, context);
        return alias;
    }
}
