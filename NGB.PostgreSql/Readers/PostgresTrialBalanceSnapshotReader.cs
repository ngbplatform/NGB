using Dapper;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Readers;

/// <summary>
/// PostgreSQL reader for Trial Balance range snapshots.
/// Pushes opening/range aggregation and dimension-scope filtering into SQL.
/// </summary>
public sealed class PostgresTrialBalanceSnapshotReader(IUnitOfWork uow) : ITrialBalanceSnapshotReader
{
    private sealed class Row
    {
        public Guid AccountId { get; init; }
        public string AccountCode { get; init; } = null!;
        public Guid DimensionSetId { get; init; }
        public decimal OpeningBalance { get; init; }
        public decimal DebitAmount { get; init; }
        public decimal CreditAmount { get; init; }
    }

    public async Task<TrialBalanceSnapshot> GetAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes,
        CancellationToken ct = default)
    {
        if (toInclusive < fromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(toInclusive), toInclusive, "To must be on or after From.");

        fromInclusive.EnsureMonthStart(nameof(fromInclusive));
        toInclusive.EnsureMonthStart(nameof(toInclusive));

        var (scopeDimIds, scopeValueIds, scopeDimensionCount) = SqlDimensionFilter.NormalizeScopes(dimensionScopes);
        var latestClosedPeriod = await GetLatestClosedPeriodAsync(fromInclusive, ct);

        IReadOnlyList<TrialBalanceSnapshotRow> rows;
        if (latestClosedPeriod is null)
        {
            rows = await LoadInceptionToDateRowsAsync(
                fromInclusive,
                toInclusive,
                scopeDimIds,
                scopeValueIds,
                scopeDimensionCount,
                ct);
        }
        else if (latestClosedPeriod.Value == fromInclusive)
        {
            rows = await LoadSnapshotOnlyRowsAsync(
                latestClosedPeriod.Value,
                fromInclusive,
                toInclusive,
                scopeDimIds,
                scopeValueIds,
                scopeDimensionCount,
                ct);
        }
        else
        {
            rows = await LoadSnapshotPlusDeltaRowsAsync(
                latestClosedPeriod.Value,
                fromInclusive,
                toInclusive,
                scopeDimIds,
                scopeValueIds,
                scopeDimensionCount,
                ct);
        }

        return new TrialBalanceSnapshot(rows);
    }

    private async Task<DateOnly?> GetLatestClosedPeriodAsync(DateOnly fromInclusive, CancellationToken ct)
    {
        const string sql = """
                           SELECT MAX(period)
                           FROM accounting_closed_periods
                           WHERE period <= @FromInclusive;
                           """;

        await uow.EnsureConnectionOpenAsync(ct);

        return await uow.Connection.ExecuteScalarAsync<DateOnly?>(
            new CommandDefinition(
                sql,
                new { FromInclusive = fromInclusive },
                transaction: uow.Transaction,
                cancellationToken: ct));
    }

    private async Task<IReadOnlyList<TrialBalanceSnapshotRow>> LoadInceptionToDateRowsAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        Guid[] scopeDimIds,
        Guid[] scopeValueIds,
        int scopeDimensionCount,
        CancellationToken ct)
    {
        var sql = $"""
                   WITH
                   {BuildScopeCtes()}
                   turnover_rows AS (
                       SELECT
                           t.account_id AS AccountId,
                           t.dimension_set_id AS DimensionSetId,
                           SUM(CASE
                               WHEN t.period < @FromInclusive::date
                                   THEN t.debit_amount - t.credit_amount
                               ELSE 0::numeric
                           END) AS OpeningBalance,
                           SUM(CASE
                               WHEN t.period >= @FromInclusive::date
                                   THEN t.debit_amount
                               ELSE 0::numeric
                           END) AS DebitAmount,
                           SUM(CASE
                               WHEN t.period >= @FromInclusive::date
                                   THEN t.credit_amount
                               ELSE 0::numeric
                           END) AS CreditAmount
                       FROM accounting_turnovers t
                       WHERE t.period <= @ToInclusive::date
                       {BuildScopeSetPredicate("t")}
                       GROUP BY t.account_id, t.dimension_set_id
                   )
                   SELECT
                       tr.AccountId,
                       a.code AS AccountCode,
                       tr.DimensionSetId,
                       tr.OpeningBalance,
                       tr.DebitAmount,
                       tr.CreditAmount
                   FROM turnover_rows tr
                   JOIN accounting_accounts a ON a.account_id = tr.AccountId AND a.is_deleted = FALSE
                   ORDER BY a.code, tr.DimensionSetId;
                   """;

        return await QueryRowsAsync(
            sql,
            new
            {
                FromInclusive = fromInclusive,
                ToInclusive = toInclusive,
                ScopeDimensionCount = scopeDimensionCount,
                ScopeDimIds = scopeDimIds,
                ScopeValueIds = scopeValueIds
            },
            ct);
    }

    private async Task<IReadOnlyList<TrialBalanceSnapshotRow>> LoadSnapshotOnlyRowsAsync(
        DateOnly snapshotPeriod,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        Guid[] scopeDimIds,
        Guid[] scopeValueIds,
        int scopeDimensionCount,
        CancellationToken ct)
    {
        var sql = $"""
                   WITH
                   {BuildScopeCtes()}
                   opening_rows AS (
                       SELECT
                           b.account_id AS AccountId,
                           b.dimension_set_id AS DimensionSetId,
                           SUM(b.opening_balance) AS OpeningBalance,
                           0::numeric AS DebitAmount,
                           0::numeric AS CreditAmount
                       FROM accounting_balances b
                       WHERE b.period = @SnapshotPeriod::date
                       {BuildScopeSetPredicate("b")}
                       GROUP BY b.account_id, b.dimension_set_id
                   ),
                   range_rows AS (
                       SELECT
                           t.account_id AS AccountId,
                           t.dimension_set_id AS DimensionSetId,
                           0::numeric AS OpeningBalance,
                           SUM(t.debit_amount) AS DebitAmount,
                           SUM(t.credit_amount) AS CreditAmount
                       FROM accounting_turnovers t
                       WHERE t.period >= @FromInclusive::date
                         AND t.period <= @ToInclusive::date
                       {BuildScopeSetPredicate("t")}
                       GROUP BY t.account_id, t.dimension_set_id
                   ),
                   final_rows AS (
                       SELECT
                           combined.AccountId,
                           combined.DimensionSetId,
                           SUM(combined.OpeningBalance) AS OpeningBalance,
                           SUM(combined.DebitAmount) AS DebitAmount,
                           SUM(combined.CreditAmount) AS CreditAmount
                       FROM (
                           SELECT * FROM opening_rows
                           UNION ALL
                           SELECT * FROM range_rows
                       ) combined
                       GROUP BY combined.AccountId, combined.DimensionSetId
                   )
                   SELECT
                       fr.AccountId,
                       a.code AS AccountCode,
                       fr.DimensionSetId,
                       fr.OpeningBalance,
                       fr.DebitAmount,
                       fr.CreditAmount
                   FROM final_rows fr
                   JOIN accounting_accounts a ON a.account_id = fr.AccountId AND a.is_deleted = FALSE
                   ORDER BY a.code, fr.DimensionSetId;
                   """;

        return await QueryRowsAsync(
            sql,
            new
            {
                SnapshotPeriod = snapshotPeriod,
                FromInclusive = fromInclusive,
                ToInclusive = toInclusive,
                ScopeDimensionCount = scopeDimensionCount,
                ScopeDimIds = scopeDimIds,
                ScopeValueIds = scopeValueIds
            },
            ct);
    }

    private async Task<IReadOnlyList<TrialBalanceSnapshotRow>> LoadSnapshotPlusDeltaRowsAsync(
        DateOnly snapshotPeriod,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        Guid[] scopeDimIds,
        Guid[] scopeValueIds,
        int scopeDimensionCount,
        CancellationToken ct)
    {
        var sql = $"""
                   WITH
                   {BuildScopeCtes()}
                   opening_snapshot_rows AS (
                       SELECT
                           b.account_id AS AccountId,
                           b.dimension_set_id AS DimensionSetId,
                           SUM(b.closing_balance) AS OpeningBalance,
                           0::numeric AS DebitAmount,
                           0::numeric AS CreditAmount
                       FROM accounting_balances b
                       WHERE b.period = @SnapshotPeriod::date
                       {BuildScopeSetPredicate("b")}
                       GROUP BY b.account_id, b.dimension_set_id
                   ),
                   turnover_delta_rows AS (
                       SELECT
                           t.account_id AS AccountId,
                           t.dimension_set_id AS DimensionSetId,
                           SUM(CASE
                               WHEN t.period < @FromInclusive::date
                                   THEN t.debit_amount - t.credit_amount
                               ELSE 0::numeric
                           END) AS OpeningBalance,
                           SUM(CASE
                               WHEN t.period >= @FromInclusive::date
                                   THEN t.debit_amount
                               ELSE 0::numeric
                           END) AS DebitAmount,
                           SUM(CASE
                               WHEN t.period >= @FromInclusive::date
                                   THEN t.credit_amount
                               ELSE 0::numeric
                           END) AS CreditAmount
                       FROM accounting_turnovers t
                       WHERE t.period > @SnapshotPeriod::date
                         AND t.period <= @ToInclusive::date
                       {BuildScopeSetPredicate("t")}
                       GROUP BY t.account_id, t.dimension_set_id
                   ),
                   final_rows AS (
                       SELECT
                           combined.AccountId,
                           combined.DimensionSetId,
                           SUM(combined.OpeningBalance) AS OpeningBalance,
                           SUM(combined.DebitAmount) AS DebitAmount,
                           SUM(combined.CreditAmount) AS CreditAmount
                       FROM (
                           SELECT * FROM opening_snapshot_rows
                           UNION ALL
                           SELECT * FROM turnover_delta_rows
                       ) combined
                       GROUP BY combined.AccountId, combined.DimensionSetId
                   )
                   SELECT
                       fr.AccountId,
                       a.code AS AccountCode,
                       fr.DimensionSetId,
                       fr.OpeningBalance,
                       fr.DebitAmount,
                       fr.CreditAmount
                   FROM final_rows fr
                   JOIN accounting_accounts a ON a.account_id = fr.AccountId AND a.is_deleted = FALSE
                   ORDER BY a.code, fr.DimensionSetId;
                   """;

        return await QueryRowsAsync(
            sql,
            new
            {
                SnapshotPeriod = snapshotPeriod,
                FromInclusive = fromInclusive,
                ToInclusive = toInclusive,
                ScopeDimensionCount = scopeDimensionCount,
                ScopeDimIds = scopeDimIds,
                ScopeValueIds = scopeValueIds
            },
            ct);
    }

    private async Task<IReadOnlyList<TrialBalanceSnapshotRow>> QueryRowsAsync(
        string sql,
        object args,
        CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        var rows = (await uow.Connection.QueryAsync<Row>(
            new CommandDefinition(
                sql,
                args,
                transaction: uow.Transaction,
                cancellationToken: ct))).AsList();

        return rows
            .Select(x => new TrialBalanceSnapshotRow(
                x.AccountId,
                x.AccountCode,
                x.DimensionSetId,
                x.OpeningBalance,
                x.DebitAmount,
                x.CreditAmount))
            .ToList();
    }

    private static string BuildScopeCtes()
        => """
           requested_scope_pairs AS (
               SELECT *
               FROM unnest(@ScopeDimIds::uuid[], @ScopeValueIds::uuid[]) AS sp(dimension_id, value_id)
           ),
           matching_dimension_sets AS (
               SELECT di.dimension_set_id
               FROM platform_dimension_set_items di
               JOIN requested_scope_pairs sp
                 ON sp.dimension_id = di.dimension_id
                AND sp.value_id = di.value_id
               GROUP BY di.dimension_set_id
               HAVING COUNT(DISTINCT di.dimension_id) = @ScopeDimensionCount::int
           ),
           """;

    private static string BuildScopeSetPredicate(string alias)
        => $"""
                   AND (
                       @ScopeDimensionCount::int = 0
                       OR {alias}.dimension_set_id IN (
                           SELECT dimension_set_id
                           FROM matching_dimension_sets
                       )
                   )
                   """;
}
