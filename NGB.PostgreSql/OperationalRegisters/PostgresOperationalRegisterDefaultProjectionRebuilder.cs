using Microsoft.Extensions.Options;
using Npgsql;
using System.Data;
using System.Text;
using Dapper;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
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
    IOperationalRegisterBalancesStore balances,
    IAdvisoryLockManager? locks = null,
    IOptions<PostgresOptions>? options = null)
    : IOperationalRegisterDefaultProjectionRebuilder, IOperationalRegisterProjectionPreparation
{
    public async Task<IOperationalRegisterPreparedProjection> PrepareMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(registerId));

        periodMonth.EnsureMonthStart(nameof(periodMonth));
        uow.EnsureActiveTransaction();

        if (uow.Transaction!.IsolationLevel != IsolationLevel.ReadCommitted)
            throw new NgbInvariantViolationException("Projection preparation requires ReadCommitted isolation.");

        if (locks is not IOperationalRegisterFinalizationLockManager publicationLocks)
            throw new NgbConfigurationViolationException("Projection preparation requires finalization lock support.");

        var timeout = options?.Value.OperationalRegisterPublicationTimeoutSeconds ?? 5;
        if (timeout <= 0)
            throw new NgbConfigurationViolationException("OperationalRegisterPublicationTimeoutSeconds must be positive.");

        var register = await registers.GetByIdAsync(registerId, ct)
            ?? throw new OperationalRegisterNotFoundException(registerId);

        if (!register.HasMovements)
            throw new NgbInvariantViolationException("Concurrent projection preparation requires immutable register resources.");

        await turnovers.EnsureReadyForWriteAsync(registerId, ct);
        await balances.EnsureReadyForWriteAsync(registerId, ct);

        var columns = (await resources.GetByRegisterIdAsync(registerId, ct))
            .OrderBy(x => x.Ordinal)
            .Select(x => x.ColumnCode)
            .ToArray();

        var source = new ProjectionSource(
            OperationalRegisterNaming.MovementsTable(register.TableCode),
            OperationalRegisterNaming.TurnoversTable(register.TableCode),
            OperationalRegisterNaming.BalancesTable(register.TableCode),
            columns);

        foreach (var name in columns.Concat([source.Movements, source.Turnovers, source.Balances]))
        {
            OperationalRegisterSqlIdentifiers.EnsureOrThrow(name, "opreg projection identifier");
        }

        // A sequence is not a commit watermark by itself. The movement store acquires ORR
        // before allocating IDs. Capture MAX only after all earlier writers have committed.
        // CACHE > 1, descending or cycling sequences would invalidate this protocol.
        var orderedSequence = await uow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
SELECT COALESCE((SELECT seqcache = 1 AND seqincrement = 1 AND NOT seqcycle
    FROM pg_sequence WHERE seqrelid = pg_get_serial_sequence(@Table, 'movement_id')::regclass), FALSE);
""",
            new
            {
                Table = source.Movements
            },
            transaction: uow.Transaction,
            cancellationToken: ct));

        if (!orderedSequence)
            throw new NgbConfigurationViolationException("Operational register movement IDs require an ascending, non-cycling sequence with CACHE 1.");

        const string savepoint = "ngb_projection_boundary";
        var transaction = uow.Transaction;

        await transaction.SaveAsync(savepoint, ct);

        long boundary;
        
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(timeout));

            try
            {
                await publicationLocks.LockOperationalRegisterPublicationAsync(registerId, periodMonth, deadline.Token);

                boundary = await uow.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    $"SELECT COALESCE(MAX(movement_id), 0) FROM {source.Movements};",
                    transaction: transaction,
                    cancellationToken: deadline.Token));
            }
            catch (Exception error) when (IsPublicationConflict(error, ct))
            {
                throw new OperationalRegisterFinalizationBusyException(registerId, periodMonth, error);
            }
        }
        finally
        {
            // PostgreSQL releases locks acquired after this savepoint. ORF, acquired by
            // the runner before the savepoint, stays held for the entire preparation.
            await transaction.RollbackAsync(savepoint, CancellationToken.None);
            await transaction.ReleaseAsync(savepoint, CancellationToken.None);
        }

        // The immutable prefix can be read at ReadCommitted while posting continues.
        // Cumulative balances come from the full movement prefix, not a possibly dirty
        // predecessor snapshot. Backdated writes between months therefore cannot lose history.
        var sql = SynchronizeMonth(source.Turnovers, AggregatePrefix(source, cumulative: false), columns)
            + SynchronizeMonth(source.Balances, AggregatePrefix(source, cumulative: true), columns);

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                PeriodMonth = periodMonth,
                Boundary = boundary
            },
            transaction: transaction,
            cancellationToken: ct));

        return new PreparedProjection(
            this,
            publicationLocks,
            source,
            registerId,
            periodMonth,
            boundary,
            transaction,
            timeout);
    }

    private static bool IsPublicationConflict(Exception error, CancellationToken caller)
        => !caller.IsCancellationRequested && error is OperationCanceledException or PostgresException { SqlState: "40P01" or "55P03" or "57014" };

    private sealed record ProjectionSource(string Movements, string Turnovers, string Balances, string[] Columns);

    private static string AggregatePrefix(ProjectionSource source, bool cumulative)
    {
        var periodFilter = cumulative ? "period_month <= @PeriodMonth" : "period_month = @PeriodMonth";
        var columns = source.Columns;
        var aggregate = string.Join(", ", columns.Select(c => $"COALESCE(SUM(CASE WHEN is_storno THEN -{c} ELSE {c} END), 0::numeric) AS {c}"));
        var having = string.Join(" OR ", columns.Select(c => $"COALESCE(SUM(CASE WHEN is_storno THEN -{c} ELSE {c} END), 0::numeric) <> 0::numeric"));

        return $"SELECT dimension_set_id{(columns.Length == 0 ? "" : ", " + aggregate)} FROM {source.Movements} "
            + $"WHERE {periodFilter} AND movement_id <= @Boundary GROUP BY dimension_set_id"
            + (columns.Length == 0 ? "" : " HAVING " + having);
    }

    private sealed class PreparedProjection(
        PostgresOperationalRegisterDefaultProjectionRebuilder owner,
        IOperationalRegisterFinalizationLockManager publicationLocks,
        ProjectionSource source, Guid registerId, DateOnly month, long boundary,
        System.Data.Common.DbTransaction transaction, int timeout) : IOperationalRegisterPreparedProjection
    {
        private bool _completed;

        public async Task CompleteAsync(CancellationToken ct = default)
        {
            if (_completed || !ReferenceEquals(owner.CurrentTransaction, transaction))
                throw new NgbInvariantViolationException("Prepared projections are single-use and cannot outlive their transaction.");

            _completed = true;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(timeout));
            try
            {
                await publicationLocks.LockOperationalRegisterPublicationAsync(registerId, month, deadline.Token);
                var sql = ApplyTail(source, source.Turnovers, cumulative: false)
                    + ApplyTail(source, source.Balances, cumulative: true);
                await owner.ExecuteTailAsync(sql, month, boundary, deadline.Token);
            }
            catch (Exception error) when (IsPublicationConflict(error, ct))
            {
                throw new OperationalRegisterFinalizationBusyException(registerId, month, error);
            }
        }
    }

    private System.Data.Common.DbTransaction? CurrentTransaction => uow.Transaction;
    private Task ExecuteTailAsync(string sql, DateOnly month, long boundary, CancellationToken ct)
        => uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                PeriodMonth = month,
                Boundary = boundary
            },
            transaction: uow.Transaction,
            cancellationToken: ct));

    private static string ApplyTail(ProjectionSource source, string target, bool cumulative)
    {
        var columns = source.Columns;
        var periodFilter = cumulative ? "period_month <= @PeriodMonth" : "period_month = @PeriodMonth";
        var filter = $"movement_id > @Boundary AND {periodFilter}";

        if (columns.Length == 0)
            return $"""
INSERT INTO {target} (period_month, dimension_set_id)
SELECT @PeriodMonth, dimension_set_id FROM {source.Movements} WHERE {filter} GROUP BY dimension_set_id
ON CONFLICT (period_month, dimension_set_id) DO NOTHING;

""";

        var names = string.Join(", ", columns);
        var aggregates = string.Join(", ", columns.Select(c => $"COALESCE(SUM(CASE WHEN is_storno THEN -{c} ELSE {c} END), 0::numeric) AS {c}"));
        var values = string.Join(", ", columns.Select(c => $"COALESCE(existing.{c}, 0::numeric) + tail.{c} AS {c}"));
        var zero = string.Join(" AND ", columns.Select(c => $"desired.{c} = 0::numeric"));
        var set = string.Join(", ", columns.Select(c => $"{c} = EXCLUDED.{c}"));
        var changed = string.Join(" OR ", columns.Select(c => $"existing.{c} IS DISTINCT FROM EXCLUDED.{c}"));

        return $"""
WITH tail AS MATERIALIZED (
    SELECT dimension_set_id, {aggregates} FROM {source.Movements} WHERE {filter} GROUP BY dimension_set_id
), desired AS MATERIALIZED (
    SELECT tail.dimension_set_id, {values}
    FROM tail LEFT JOIN {target} existing
      ON existing.period_month = @PeriodMonth AND existing.dimension_set_id = tail.dimension_set_id
), removed AS (
    DELETE FROM {target} existing USING desired
    WHERE existing.period_month = @PeriodMonth AND existing.dimension_set_id = desired.dimension_set_id AND {zero}
)
INSERT INTO {target} AS existing (period_month, dimension_set_id, {names})
SELECT @PeriodMonth, dimension_set_id, {names} FROM desired WHERE NOT ({zero})
ON CONFLICT (period_month, dimension_set_id) DO UPDATE SET {set} WHERE {changed};

""";
    }

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
        var columns = string.Join(", ", resources);
        var zeros = string.Join(", ", resources.Select(column => $"0::numeric AS {column}"));
        var empty = "SELECT NULL::uuid AS dimension_set_id" + (resources.Count == 0 ? "" : $", {zeros}") + " WHERE FALSE";
        var source = empty;
        if (movementsTable is not null)
        {
            var aggregates = string.Join(", ", resources.Select(column =>
                $"COALESCE(SUM(CASE WHEN is_storno THEN -{column} ELSE {column} END), 0::numeric) AS {column}"));
            var nonZero = string.Join(" OR ", resources.Select(column =>
                $"COALESCE(SUM(CASE WHEN is_storno THEN -{column} ELSE {column} END), 0::numeric) <> 0::numeric"));
            source = $"SELECT dimension_set_id{(resources.Count == 0 ? "" : $", {aggregates}")} "
                + $"FROM {movementsTable} WHERE period_month = @PeriodMonth GROUP BY dimension_set_id"
                + (resources.Count == 0 ? "" : $" HAVING {nonZero}");
        }

        var sql = new StringBuilder(SynchronizeMonth(turnoversTable, source, resources));
        var previous = hasPreviousPeriod
            ? $"SELECT dimension_set_id{(resources.Count == 0 ? "" : $", {columns}")} FROM {balancesTable} WHERE period_month = @PreviousPeriod"
            : empty;

        if (resources.Count == 0)
        {
            source = $"{previous} UNION SELECT dimension_set_id FROM {turnoversTable} WHERE period_month = @PeriodMonth";
        }
        else
        {
            var combined = string.Join(", ", resources.Select(column =>
                $"COALESCE(p.{column}, 0::numeric) + COALESCE(t.{column}, 0::numeric) AS {column}"));
            var nonZero = string.Join(" OR ", resources.Select(column =>
                $"COALESCE(p.{column}, 0::numeric) + COALESCE(t.{column}, 0::numeric) <> 0::numeric"));
            source = $"""
SELECT COALESCE(p.dimension_set_id, t.dimension_set_id) AS dimension_set_id, {combined}
FROM ({previous}) p
FULL JOIN (
    SELECT dimension_set_id, {columns} FROM {turnoversTable} WHERE period_month = @PeriodMonth
) t USING (dimension_set_id)
WHERE {nonZero}
""";
        }
        sql.Append(SynchronizeMonth(balancesTable, source, resources));

        return sql.ToString();
    }

    private static string SynchronizeMonth(string table, string source, IReadOnlyList<string> resources)
    {
        var columns = "period_month, dimension_set_id" + (resources.Count == 0 ? "" : $", {string.Join(", ", resources)}");
        var sameValues = resources.Count == 0 ? "" : " AND "
            + string.Join(" AND ", resources.Select(column => $"existing.{column} IS NOT DISTINCT FROM desired.{column}"));
        var update = resources.Count == 0 ? "DO NOTHING" :
            "DO UPDATE SET " + string.Join(", ", resources.Select(column => $"{column} = EXCLUDED.{column}"))
            + " WHERE " + string.Join(" OR ", resources.Select(column => $"target.{column} IS DISTINCT FROM EXCLUDED.{column}"));
        // The desired rows are materialized once. Remove absent keys and upsert only changed
        // values: unchanged carry-forward balances must not generate a full month's WAL again.

        return $"""
WITH desired AS MATERIALIZED (
    SELECT @PeriodMonth::date AS period_month, projected.* FROM ({source}) projected
), removed AS (
    DELETE FROM {table} target
    WHERE target.period_month = @PeriodMonth
      AND NOT EXISTS (SELECT 1 FROM desired WHERE desired.dimension_set_id = target.dimension_set_id)
)
INSERT INTO {table} AS target ({columns})
SELECT {columns} FROM desired
WHERE NOT EXISTS (
    SELECT 1 FROM {table} existing
    WHERE existing.period_month = @PeriodMonth
      AND existing.dimension_set_id = desired.dimension_set_id{sameValues}
)
ON CONFLICT (period_month, dimension_set_id) {update};

""";
    }
}
