using Dapper;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.Schema;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Aggregates a month-local net projection from a per-register movements table.
/// </summary>
public sealed class PostgresOperationalRegisterMonthlyProjectionAggregator(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceRepository resources,
    OperationalRegisterMetadataCache? metadataCache = null,
    PostgresRelationPresenceCache? relationPresenceCache = null)
    : IOperationalRegisterMonthlyProjectionAggregator
{
    private readonly OperationalRegisterMetadataCache _metadataCache = metadataCache
        ?? new OperationalRegisterMetadataCache(TimeProvider.System);
    private readonly PostgresRelationPresenceCache _relationPresenceCache = relationPresenceCache
        ?? new PostgresRelationPresenceCache(TimeProvider.System);
    public async Task<IReadOnlyList<OperationalRegisterMonthlyProjectionRow>> AggregateMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(registerId));

        periodMonth.EnsureMonthStart(nameof(periodMonth));

        await uow.EnsureConnectionOpenAsync(ct);

        var metadata = await _metadataCache.GetOrCreateAsync(
            registerId,
            loadCt => LoadMetadataAsync(registerId, loadCt),
            ct);
        var tableName = metadata.MovementsTable;
        var resourceColumns = metadata.Resources
            .Select(static resource => resource.ColumnCode)
            .ToArray();

        if (!await _relationPresenceCache.ExistsAsync(
                tableName,
                probeCt => PostgresTableExistence.ExistsAsync(uow, tableName, probeCt),
                ct))
            return [];

        var resourceSelect = resourceColumns.Length == 0
            ? string.Empty
            : ", " + string.Join(", ", resourceColumns.Select(column =>
                $"COALESCE(SUM(CASE WHEN is_storno THEN -{column} ELSE {column} END), 0) AS \"{column}\""));

        var sql = resourceColumns.Length == 0
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

            if (resourceColumns.Length > 0 && values.Values.All(v => v == 0m))
                continue;

            result.Add(new OperationalRegisterMonthlyProjectionRow(dimensionSetId, values));
        }

        return result;
    }

    private async Task<OperationalRegisterMetadataContext> LoadMetadataAsync(Guid registerId, CancellationToken ct)
    {
        var register = await registers.GetByIdAsync(registerId, ct)
            ?? throw new OperationalRegisterNotFoundException(registerId);
        var tableName = OperationalRegisterNaming.MovementsTable(register.TableCode);

        OperationalRegisterSqlIdentifiers.EnsureOrThrow(tableName, "opreg movements table name");

        var resourceDefinitions = (await resources.GetByRegisterIdAsync(registerId, ct))
            .OrderBy(static resource => resource.Ordinal)
            .ToArray();

        foreach (var resource in resourceDefinitions)
        {
            OperationalRegisterSqlIdentifiers.EnsureOrThrow(resource.ColumnCode, "opreg resource column_code");
        }

        return new OperationalRegisterMetadataContext(register, resourceDefinitions, tableName);
    }
}
