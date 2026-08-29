using System.Text;
using Dapper;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// PostgreSQL set-based implementation of the default operational-register projection.
/// The complete dimension/resource matrix remains in PostgreSQL.
/// </summary>
public sealed class PostgresOperationalRegisterDefaultProjectionRebuilder(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceRepository resources,
    IOperationalRegisterTurnoversStore turnovers,
    IOperationalRegisterBalancesStore balances)
    : IOperationalRegisterDefaultProjectionRebuilder
{
    public async Task RebuildMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        DateOnly? previousFinalizedPeriod,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(registerId));

        periodMonth.EnsureMonthStart(nameof(periodMonth));
        previousFinalizedPeriod?.EnsureMonthStart(nameof(previousFinalizedPeriod));
        uow.EnsureActiveTransaction();

        // Schema readiness is retained here; hot-path DDL is removed separately by the readiness cache.
        await turnovers.EnsureReadyForWriteAsync(registerId, ct);
        await balances.EnsureReadyForWriteAsync(registerId, ct);

        var register = await registers.GetByIdAsync(registerId, ct)
            ?? throw new OperationalRegisterNotFoundException(registerId);

        var resourceColumns = (await resources.GetByRegisterIdAsync(registerId, ct))
            .OrderBy(x => x.Ordinal)
            .Select(x => x.ColumnCode)
            .ToArray();

        var movementsTable = OperationalRegisterNaming.MovementsTable(register.TableCode);
        var turnoversTable = OperationalRegisterNaming.TurnoversTable(register.TableCode);
        var balancesTable = OperationalRegisterNaming.BalancesTable(register.TableCode);
        OperationalRegisterSqlIdentifiers.EnsureOrThrow(movementsTable, "opreg movements table name");
        OperationalRegisterSqlIdentifiers.EnsureOrThrow(turnoversTable, "opreg turnovers table name");
        OperationalRegisterSqlIdentifiers.EnsureOrThrow(balancesTable, "opreg balances table name");

        foreach (var column in resourceColumns)
        {
            OperationalRegisterSqlIdentifiers.EnsureOrThrow(column, "opreg resource column_code");
        }

        var movementsExist = await PostgresTableExistence.ExistsAsync(uow, movementsTable, ct);
        var sql = BuildSql(
            movementsExist ? movementsTable : null,
            turnoversTable,
            balancesTable,
            resourceColumns,
            previousFinalizedPeriod.HasValue);

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { PeriodMonth = periodMonth, PreviousPeriod = previousFinalizedPeriod },
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    private static string BuildSql(
        string? movementsTable,
        string turnoversTable,
        string balancesTable,
        IReadOnlyList<string> resources,
        bool hasPreviousPeriod)
    {
        var sql = new StringBuilder();
        sql.AppendLine($"DELETE FROM {turnoversTable} WHERE period_month = @PeriodMonth;");

        if (movementsTable is not null)
        {
            if (resources.Count == 0)
            {
                sql.AppendLine($"""
INSERT INTO {turnoversTable} (period_month, dimension_set_id)
SELECT @PeriodMonth, dimension_set_id
FROM {movementsTable}
WHERE period_month = @PeriodMonth
GROUP BY dimension_set_id;
""");
            }
            else
            {
                var aggregates = string.Join(", ", resources.Select(column =>
                    $"COALESCE(SUM(CASE WHEN is_storno THEN -{column} ELSE {column} END), 0::numeric)"));
                var nonZero = string.Join(" OR ", resources.Select(column =>
                    $"COALESCE(SUM(CASE WHEN is_storno THEN -{column} ELSE {column} END), 0::numeric) <> 0::numeric"));
                sql.AppendLine($"""
INSERT INTO {turnoversTable} (period_month, dimension_set_id, {string.Join(", ", resources)})
SELECT @PeriodMonth, dimension_set_id, {aggregates}
FROM {movementsTable}
WHERE period_month = @PeriodMonth
GROUP BY dimension_set_id
HAVING {nonZero};
""");
            }
        }

        sql.AppendLine($"DELETE FROM {balancesTable} WHERE period_month = @PeriodMonth;");
        if (resources.Count == 0)
        {
            var previous = hasPreviousPeriod
                ? $"SELECT dimension_set_id FROM {balancesTable} WHERE period_month = @PreviousPeriod"
                : "SELECT NULL::uuid AS dimension_set_id WHERE FALSE";
            sql.AppendLine($"""
INSERT INTO {balancesTable} (period_month, dimension_set_id)
SELECT @PeriodMonth, dimension_set_id
FROM (
    {previous}
    UNION
    SELECT dimension_set_id FROM {turnoversTable} WHERE period_month = @PeriodMonth
) keys;
""");
            return sql.ToString();
        }

        var previousColumns = hasPreviousPeriod
            ? string.Join(", ", resources)
            : string.Join(", ", resources.Select(column => $"0::numeric AS {column}"));
        var previousSource = hasPreviousPeriod
            ? $"SELECT dimension_set_id, {previousColumns} FROM {balancesTable} WHERE period_month = @PreviousPeriod"
            : $"SELECT NULL::uuid AS dimension_set_id, {previousColumns} WHERE FALSE";
        var combinedColumns = string.Join(", ", resources.Select(column =>
            $"COALESCE(p.{column}, 0::numeric) + COALESCE(t.{column}, 0::numeric) AS {column}"));
        var nonZeroCombined = string.Join(" OR ", resources.Select(column =>
            $"COALESCE(p.{column}, 0::numeric) + COALESCE(t.{column}, 0::numeric) <> 0::numeric"));

        sql.AppendLine($"""
INSERT INTO {balancesTable} (period_month, dimension_set_id, {string.Join(", ", resources)})
SELECT
    @PeriodMonth,
    COALESCE(p.dimension_set_id, t.dimension_set_id),
    {combinedColumns}
FROM ({previousSource}) p
FULL JOIN (
    SELECT dimension_set_id, {string.Join(", ", resources)}
    FROM {turnoversTable}
    WHERE period_month = @PeriodMonth
) t USING (dimension_set_id)
WHERE {nonZeroCombined};
""");
        return sql.ToString();
    }
}
