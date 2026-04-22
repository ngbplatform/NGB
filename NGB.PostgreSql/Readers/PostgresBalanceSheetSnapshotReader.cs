using Dapper;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Readers;

/// <summary>
/// PostgreSQL reader for Balance Sheet "as of" snapshots.
/// Pushes period aggregation and dimension-scope filtering into SQL and returns account-level balances.
/// </summary>
public sealed class PostgresBalanceSheetSnapshotReader(IUnitOfWork uow) : IBalanceSheetSnapshotReader
{
    private sealed class Row
    {
        public Guid AccountId { get; init; }
        public decimal ClosingBalance { get; init; }
    }

    public async Task<BalanceSheetSnapshot> GetAsync(
        DateOnly asOfPeriod,
        DimensionScopeBag? dimensionScopes,
        CancellationToken ct = default)
    {
        var (scopeDimIds, scopeValueIds, scopeDimensionCount) = SqlDimensionFilter.NormalizeScopes(dimensionScopes);
        var latestClosedPeriod = await GetLatestClosedPeriodAsync(asOfPeriod, ct);
        var rollForwardPeriods = latestClosedPeriod is null
            ? 0
            : CountPeriods(latestClosedPeriod.Value.AddMonths(1), asOfPeriod);

        IReadOnlyList<BalanceSheetSnapshotRow> rows;
        if (latestClosedPeriod is null)
        {
            rows = await LoadInceptionToDateRowsAsync(
                asOfPeriod,
                scopeDimIds,
                scopeValueIds,
                scopeDimensionCount,
                ct);
        }
        else if (latestClosedPeriod.Value == asOfPeriod)
        {
            rows = await LoadSnapshotOnlyRowsAsync(
                latestClosedPeriod.Value,
                scopeDimIds,
                scopeValueIds,
                scopeDimensionCount,
                ct);
        }
        else
        {
            rows = await LoadSnapshotPlusDeltaRowsAsync(
                latestClosedPeriod.Value,
                asOfPeriod,
                scopeDimIds,
                scopeValueIds,
                scopeDimensionCount,
                ct);
        }

        return new BalanceSheetSnapshot(rows, latestClosedPeriod, rollForwardPeriods);
    }

    private async Task<DateOnly?> GetLatestClosedPeriodAsync(DateOnly asOfPeriod, CancellationToken ct)
    {
        const string sql = """
                           SELECT MAX(period)
                           FROM accounting_closed_periods
                           WHERE period <= @AsOfPeriod;
                           """;

        await uow.EnsureConnectionOpenAsync(ct);

        return await uow.Connection.ExecuteScalarAsync<DateOnly?>(
            new CommandDefinition(
                sql,
                new { AsOfPeriod = asOfPeriod },
                transaction: uow.Transaction,
                cancellationToken: ct));
    }

    private async Task<IReadOnlyList<BalanceSheetSnapshotRow>> LoadSnapshotOnlyRowsAsync(
        DateOnly snapshotPeriod,
        Guid[] scopeDimIds,
        Guid[] scopeValueIds,
        int scopeDimensionCount,
        CancellationToken ct)
    {
        var sql = $"""
                   SELECT
                       b.account_id AS AccountId,
                       SUM(b.closing_balance) AS ClosingBalance
                   FROM accounting_balances b
                   WHERE b.period = @SnapshotPeriod::date
                   {BuildScopePredicate("b")}
                   GROUP BY b.account_id
                   ORDER BY b.account_id;
                   """;

        return await QueryRowsAsync(
            sql,
            new
            {
                SnapshotPeriod = snapshotPeriod,
                ScopeDimensionCount = scopeDimensionCount,
                ScopeDimIds = scopeDimIds,
                ScopeValueIds = scopeValueIds
            },
            ct);
    }

    private async Task<IReadOnlyList<BalanceSheetSnapshotRow>> LoadInceptionToDateRowsAsync(
        DateOnly asOfPeriod,
        Guid[] scopeDimIds,
        Guid[] scopeValueIds,
        int scopeDimensionCount,
        CancellationToken ct)
    {
        var sql = $"""
                   SELECT
                       t.account_id AS AccountId,
                       SUM(t.debit_amount - t.credit_amount) AS ClosingBalance
                   FROM accounting_turnovers t
                   WHERE t.period <= @AsOfPeriod::date
                   {BuildScopePredicate("t")}
                   GROUP BY t.account_id
                   ORDER BY t.account_id;
                   """;

        return await QueryRowsAsync(
            sql,
            new
            {
                AsOfPeriod = asOfPeriod,
                ScopeDimensionCount = scopeDimensionCount,
                ScopeDimIds = scopeDimIds,
                ScopeValueIds = scopeValueIds
            },
            ct);
    }

    private async Task<IReadOnlyList<BalanceSheetSnapshotRow>> LoadSnapshotPlusDeltaRowsAsync(
        DateOnly snapshotPeriod,
        DateOnly asOfPeriod,
        Guid[] scopeDimIds,
        Guid[] scopeValueIds,
        int scopeDimensionCount,
        CancellationToken ct)
    {
        var sql = $"""
                   WITH snapshot_rows AS (
                       SELECT
                           b.account_id AS AccountId,
                           SUM(b.closing_balance) AS ClosingBalance
                       FROM accounting_balances b
                       WHERE b.period = @SnapshotPeriod::date
                       {BuildScopePredicate("b")}
                       GROUP BY b.account_id
                   ),
                   delta_rows AS (
                       SELECT
                           t.account_id AS AccountId,
                           SUM(t.debit_amount - t.credit_amount) AS ClosingBalance
                       FROM accounting_turnovers t
                       WHERE t.period >= @TurnoverFrom::date
                         AND t.period <= @AsOfPeriod::date
                       {BuildScopePredicate("t")}
                       GROUP BY t.account_id
                   )
                   SELECT
                       combined.AccountId,
                       SUM(combined.ClosingBalance) AS ClosingBalance
                   FROM (
                       SELECT AccountId, ClosingBalance FROM snapshot_rows
                       UNION ALL
                       SELECT AccountId, ClosingBalance FROM delta_rows
                   ) combined
                   GROUP BY combined.AccountId
                   ORDER BY combined.AccountId;
                   """;

        return await QueryRowsAsync(
            sql,
            new
            {
                SnapshotPeriod = snapshotPeriod,
                TurnoverFrom = snapshotPeriod.AddMonths(1),
                AsOfPeriod = asOfPeriod,
                ScopeDimensionCount = scopeDimensionCount,
                ScopeDimIds = scopeDimIds,
                ScopeValueIds = scopeValueIds
            },
            ct);
    }

    private async Task<IReadOnlyList<BalanceSheetSnapshotRow>> QueryRowsAsync(
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
            .Select(x => new BalanceSheetSnapshotRow(x.AccountId, x.ClosingBalance))
            .ToList();
    }

    private static string BuildScopePredicate(string alias)
        => $"""
                   AND (
                       @ScopeDimensionCount::int = 0
                       OR (
                           SELECT COUNT(DISTINCT req.dimension_id)
                           FROM platform_dimension_set_items di
                           JOIN (
                               SELECT
                                   unnest(@ScopeDimIds::uuid[]) AS dimension_id,
                                   unnest(@ScopeValueIds::uuid[]) AS value_id
                           ) req ON req.dimension_id = di.dimension_id AND req.value_id = di.value_id
                           WHERE di.dimension_set_id = {alias}.dimension_set_id
                       ) = @ScopeDimensionCount::int
                   )
                   """;

    private static int CountPeriods(DateOnly fromInclusive, DateOnly toInclusive)
    {
        if (fromInclusive > toInclusive)
            return 0;

        return (toInclusive.Year - fromInclusive.Year) * 12 + toInclusive.Month - fromInclusive.Month + 1;
    }
}
