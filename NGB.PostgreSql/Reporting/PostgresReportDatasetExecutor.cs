using Dapper;
using NGB.Contracts.Common;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Reporting;

public sealed class PostgresReportDatasetExecutor(IUnitOfWork uow, PostgresReportSqlBuilder sqlBuilder)
{
    private readonly IUnitOfWork _uow = uow ?? throw new NgbConfigurationViolationException("PostgreSQL reporting executor requires a unit of work registration.");
    private readonly PostgresReportSqlBuilder _sqlBuilder = sqlBuilder ?? throw new NgbConfigurationViolationException("PostgreSQL reporting executor requires a SQL builder registration.");

    public async Task<PostgresReportExecutionResult> ExecuteAsync(
        PostgresReportExecutionRequest request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var statement = _sqlBuilder.Build(request);
        await _uow.EnsureConnectionOpenAsync(ct);

        var rows = (await _uow.Connection.QueryAsync(
            new CommandDefinition(
                statement.Sql,
                statement.Parameters,
                _uow.Transaction,
                cancellationToken: ct))).ToList();

        if (request.Paging.DisablePaging)
            EnsureMaterializationBound(rows.Count);

        var hasMore = !request.Paging.DisablePaging && rows.Count > request.Paging.Limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var materializedWithCursorKeys = rows.Select(MaterializeRow).ToList();
        var nextCursor = hasMore && statement.CursorColumns.Count > 0 && materializedWithCursorKeys.Count > 0
            ? PostgresReportCursorCodec.Encode(statement.DatasetCode, statement.CursorColumns, materializedWithCursorKeys[^1])
            : null;
        var hiddenAliases = statement.CursorColumns
            .Where(x => x.IsHidden)
            .Select(x => x.Alias)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var materialized = new List<PostgresReportExecutionRow>(materializedWithCursorKeys.Count);
        foreach (var values in materializedWithCursorKeys)
        {
            IReadOnlyDictionary<string, object?> visibleValues = values;
            if (hiddenAliases.Count > 0)
            {
                visibleValues = new Dictionary<string, object?>(
                    values.Where(x => !hiddenAliases.Contains(x.Key)),
                    StringComparer.OrdinalIgnoreCase);
            }

            materialized.Add(new PostgresReportExecutionRow(visibleValues));
        }

        return new PostgresReportExecutionResult(
            Columns: statement.Columns,
            Rows: materialized,
            Offset: request.Paging.DisablePaging ? 0 : statement.Offset,
            Limit: request.Paging.DisablePaging ? materialized.Count : statement.Limit,
            HasMore: hasMore,
            NextCursor: nextCursor,
            Total: request.Paging.DisablePaging ? materialized.Count : null,
            Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "postgres-foundation",
                ["aggregated"] = statement.IsAggregated.ToString(),
                ["rowCount"] = materialized.Count.ToString()
            });
    }

    internal static IReadOnlyDictionary<string, object?> MaterializeRow(object row)
    {
        if (row is IDictionary<string, object?> typed)
            return new Dictionary<string, object?>(typed, StringComparer.OrdinalIgnoreCase);

        throw new NgbInvariantViolationException("PostgreSQL reporting executor expected Dapper row materialization to provide a dictionary payload.");
    }

    internal static void EnsureMaterializationBound(int rowCount)
    {
        if (rowCount <= PagingLimits.MaxMaterializedRows)
            return;

        throw new NgbArgumentOutOfRangeException(
            "rows",
            rowCount,
            $"A composable report can materialize at most {PagingLimits.MaxMaterializedRows:N0} rows. Narrow the filters or use paging.");
    }
}
