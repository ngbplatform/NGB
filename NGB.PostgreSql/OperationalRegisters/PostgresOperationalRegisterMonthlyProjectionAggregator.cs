using Dapper;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.OperationalRegisters.Internal;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Aggregates a month-local net projection from a per-register movements table.
/// </summary>
public sealed class PostgresOperationalRegisterMonthlyProjectionAggregator(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceRepository resources)
    : IOperationalRegisterMonthlyProjectionAggregator
{
    public async Task<IReadOnlyList<OperationalRegisterMonthlyProjectionRow>> AggregateMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(registerId));

        periodMonth.EnsureMonthStart(nameof(periodMonth));

        await uow.EnsureConnectionOpenAsync(ct);

        var (tableName, resourceColumns) =
            await OperationalRegisterMovementsTableResolver.ResolveOrThrowAsync(registers, resources, registerId, ct);

        if (!await PostgresTableExistence.ExistsAsync(uow, tableName, ct))
            return [];

        var resourceSelect = resourceColumns.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", resourceColumns.Select(column =>
                $"COALESCE(SUM(CASE WHEN is_storno THEN -{column} ELSE {column} END), 0) AS \"{column}\""));

        var sql = resourceColumns.Count == 0
            ? $"""
               SELECT
                   dimension_set_id AS "DimensionSetId"
               FROM {tableName}
               WHERE period_month = @PeriodMonth
               GROUP BY dimension_set_id
               ORDER BY dimension_set_id;
               """
            : $"""
               SELECT
                   dimension_set_id AS "DimensionSetId"{resourceSelect}
               FROM {tableName}
               WHERE period_month = @PeriodMonth
               GROUP BY dimension_set_id
               ORDER BY dimension_set_id;
               """;

        var cmd = new CommandDefinition(
            sql,
            new { PeriodMonth = periodMonth },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync(cmd);
        var result = new List<OperationalRegisterMonthlyProjectionRow>();

        foreach (var row in rows)
        {
            var data = (IDictionary<string, object?>)row;
            var dimensionSetId = (Guid)data["DimensionSetId"]!;
            var values = new Dictionary<string, decimal>(StringComparer.Ordinal);

            foreach (var column in resourceColumns)
            {
                var raw = data.TryGetValue(column, out var value) ? value : null;
                values[column] = raw is null or DBNull ? 0m : Convert.ToDecimal(raw);
            }

            if (resourceColumns.Count > 0 && values.Values.All(v => v == 0m))
                continue;

            result.Add(new OperationalRegisterMonthlyProjectionRow(dimensionSetId, values));
        }

        return result;
    }
}
