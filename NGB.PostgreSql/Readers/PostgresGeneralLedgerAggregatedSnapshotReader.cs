using Dapper;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Readers;

/// <summary>
/// PostgreSQL summary reader for General Ledger (Aggregated).
/// Computes opening and full-range totals via balances + turnovers with SQL-side dimension filtering.
/// </summary>
public sealed class PostgresGeneralLedgerAggregatedSnapshotReader(IUnitOfWork uow)
    : IGeneralLedgerAggregatedSnapshotReader
{
    private sealed class Row
    {
        public string? AccountCode { get; init; }
        public decimal OpeningBalance { get; init; }
        public decimal TotalDebit { get; init; }
        public decimal TotalCredit { get; init; }
    }

    public async Task<GeneralLedgerAggregatedSnapshot> GetAsync(
        Guid accountId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes,
        CancellationToken ct = default)
    {
        if (accountId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(accountId));

        if (toInclusive < fromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(toInclusive), toInclusive, "To must be on or after From.");

        fromInclusive.EnsureMonthStart(nameof(fromInclusive));
        toInclusive.EnsureMonthStart(nameof(toInclusive));

        var (scopeDimIds, scopeValueIds, scopeDimensionCount) = SqlDimensionFilter.NormalizeScopes(dimensionScopes);
        var latestClosedPeriod = await GetLatestClosedPeriodAsync(fromInclusive, ct);

        var row = latestClosedPeriod is null
            ? await LoadInceptionToDateAsync(accountId, fromInclusive, toInclusive, scopeDimIds, scopeValueIds, scopeDimensionCount, ct)
            : latestClosedPeriod.Value == fromInclusive
                ? await LoadSnapshotOnlyAsync(accountId, latestClosedPeriod.Value, fromInclusive, toInclusive, scopeDimIds, scopeValueIds, scopeDimensionCount, ct)
                : await LoadSnapshotPlusDeltaAsync(accountId, latestClosedPeriod.Value, fromInclusive, toInclusive, scopeDimIds, scopeValueIds, scopeDimensionCount, ct);

        return new GeneralLedgerAggregatedSnapshot(
            row.AccountCode ?? string.Empty,
            row.OpeningBalance,
            row.TotalDebit,
            row.TotalCredit);
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

    private async Task<Row> LoadInceptionToDateAsync(
        Guid accountId,
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
                   summary AS (
                       SELECT
                           COALESCE(SUM(CASE
                               WHEN t.period < @FromInclusive::date
                                   THEN t.debit_amount - t.credit_amount
                               ELSE 0::numeric
                           END), 0::numeric) AS OpeningBalance,
                           COALESCE(SUM(CASE
                               WHEN t.period >= @FromInclusive::date
                                   THEN t.debit_amount
                               ELSE 0::numeric
                           END), 0::numeric) AS TotalDebit,
                           COALESCE(SUM(CASE
                               WHEN t.period >= @FromInclusive::date
                                   THEN t.credit_amount
                               ELSE 0::numeric
                           END), 0::numeric) AS TotalCredit
                       FROM accounting_turnovers t
                       WHERE t.account_id = @AccountId::uuid
                         AND t.period <= @ToInclusive::date
                       {BuildScopeSetPredicate("t")}
                   )
                   SELECT
                       a.code AS AccountCode,
                       s.OpeningBalance,
                       s.TotalDebit,
                       s.TotalCredit
                   FROM summary s
                   LEFT JOIN accounting_accounts a
                     ON a.account_id = @AccountId::uuid
                    AND a.is_deleted = FALSE;
                   """;

        return await QuerySingleAsync(
            sql,
            new
            {
                AccountId = accountId,
                FromInclusive = fromInclusive,
                ToInclusive = toInclusive,
                ScopeDimensionCount = scopeDimensionCount,
                ScopeDimIds = scopeDimIds,
                ScopeValueIds = scopeValueIds
            },
            ct);
    }

    private async Task<Row> LoadSnapshotOnlyAsync(
        Guid accountId,
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
                   opening_snapshot AS (
                       SELECT
                           COALESCE(SUM(b.opening_balance), 0::numeric) AS OpeningBalance
                       FROM accounting_balances b
                       WHERE b.period = @SnapshotPeriod::date
                         AND b.account_id = @AccountId::uuid
                       {BuildScopeSetPredicate("b")}
                   ),
                   range_totals AS (
                       SELECT
                           COALESCE(SUM(t.debit_amount), 0::numeric) AS TotalDebit,
                           COALESCE(SUM(t.credit_amount), 0::numeric) AS TotalCredit
                       FROM accounting_turnovers t
                       WHERE t.account_id = @AccountId::uuid
                         AND t.period >= @FromInclusive::date
                         AND t.period <= @ToInclusive::date
                       {BuildScopeSetPredicate("t")}
                   )
                   SELECT
                       a.code AS AccountCode,
                       o.OpeningBalance,
                       r.TotalDebit,
                       r.TotalCredit
                   FROM opening_snapshot o
                   CROSS JOIN range_totals r
                   LEFT JOIN accounting_accounts a
                     ON a.account_id = @AccountId::uuid
                    AND a.is_deleted = FALSE;
                   """;

        return await QuerySingleAsync(
            sql,
            new
            {
                AccountId = accountId,
                SnapshotPeriod = snapshotPeriod,
                FromInclusive = fromInclusive,
                ToInclusive = toInclusive,
                ScopeDimensionCount = scopeDimensionCount,
                ScopeDimIds = scopeDimIds,
                ScopeValueIds = scopeValueIds
            },
            ct);
    }

    private async Task<Row> LoadSnapshotPlusDeltaAsync(
        Guid accountId,
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
                   opening_snapshot AS (
                       SELECT
                           COALESCE(SUM(b.closing_balance), 0::numeric) AS SnapshotClosingBalance
                       FROM accounting_balances b
                       WHERE b.period = @SnapshotPeriod::date
                         AND b.account_id = @AccountId::uuid
                       {BuildScopeSetPredicate("b")}
                   ),
                   turnover_delta AS (
                       SELECT
                           COALESCE(SUM(CASE
                               WHEN t.period < @FromInclusive::date
                                   THEN t.debit_amount - t.credit_amount
                               ELSE 0::numeric
                           END), 0::numeric) AS OpeningDelta,
                           COALESCE(SUM(CASE
                               WHEN t.period >= @FromInclusive::date
                                   THEN t.debit_amount
                               ELSE 0::numeric
                           END), 0::numeric) AS TotalDebit,
                           COALESCE(SUM(CASE
                               WHEN t.period >= @FromInclusive::date
                                   THEN t.credit_amount
                               ELSE 0::numeric
                           END), 0::numeric) AS TotalCredit
                       FROM accounting_turnovers t
                       WHERE t.account_id = @AccountId::uuid
                         AND t.period > @SnapshotPeriod::date
                         AND t.period <= @ToInclusive::date
                       {BuildScopeSetPredicate("t")}
                   )
                   SELECT
                       a.code AS AccountCode,
                       o.SnapshotClosingBalance + d.OpeningDelta AS OpeningBalance,
                       d.TotalDebit,
                       d.TotalCredit
                   FROM opening_snapshot o
                   CROSS JOIN turnover_delta d
                   LEFT JOIN accounting_accounts a
                     ON a.account_id = @AccountId::uuid
                    AND a.is_deleted = FALSE;
                   """;

        return await QuerySingleAsync(
            sql,
            new
            {
                AccountId = accountId,
                SnapshotPeriod = snapshotPeriod,
                FromInclusive = fromInclusive,
                ToInclusive = toInclusive,
                ScopeDimensionCount = scopeDimensionCount,
                ScopeDimIds = scopeDimIds,
                ScopeValueIds = scopeValueIds
            },
            ct);
    }

    private async Task<Row> QuerySingleAsync(string sql, object args, CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        return await uow.Connection.QuerySingleAsync<Row>(
            new CommandDefinition(
                sql,
                args,
                transaction: uow.Transaction,
                cancellationToken: ct));
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
