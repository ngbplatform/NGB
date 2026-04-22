using Dapper;
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

        var hasMore = !request.Paging.DisablePaging && rows.Count > request.Paging.Limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var materialized = rows
            .Select(MaterializeRow)
            .Select(static x => new PostgresReportExecutionRow(x))
            .ToList();

        return new PostgresReportExecutionResult(
            Columns: statement.Columns,
            Rows: materialized,
            Offset: request.Paging.DisablePaging ? 0 : statement.Offset,
            Limit: request.Paging.DisablePaging ? materialized.Count : statement.Limit,
            HasMore: hasMore,
            NextCursor: null,
            Total: request.Paging.DisablePaging ? materialized.Count : null,
            Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "postgres-foundation",
                ["aggregated"] = statement.IsAggregated.ToString(),
                ["rowCount"] = materialized.Count.ToString()
            });
    }

    private static IReadOnlyDictionary<string, object?> MaterializeRow(dynamic row)
    {
        if (row is IDictionary<string, object?> typed)
            return new Dictionary<string, object?>(typed, StringComparer.OrdinalIgnoreCase);

        if (row is IDictionary<string, object> boxed)
            return boxed.ToDictionary(x => x.Key, object? (x) => x.Value, StringComparer.OrdinalIgnoreCase);

        throw new NgbInvariantViolationException("PostgreSQL reporting executor expected Dapper row materialization to provide a dictionary payload.");
    }
}
