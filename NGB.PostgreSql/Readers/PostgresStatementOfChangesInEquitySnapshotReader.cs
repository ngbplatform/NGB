using Dapper;
using NGB.Accounting.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Readers;

/// <summary>
/// PostgreSQL reader for Statement of Changes in Equity endpoint snapshots.
/// Computes opening and closing "as of" states for Equity and P&amp;L sections using balances snapshots plus turnover roll-forward.
/// </summary>
public sealed class PostgresStatementOfChangesInEquitySnapshotReader(IUnitOfWork uow)
    : IStatementOfChangesInEquitySnapshotReader
{
    private static readonly short[] RelevantSections =
    [
        (short)StatementSection.Equity,
        (short)StatementSection.Income,
        (short)StatementSection.CostOfGoodsSold,
        (short)StatementSection.Expenses,
        (short)StatementSection.OtherIncome,
        (short)StatementSection.OtherExpense
    ];

    private sealed class LatestClosedPeriodsRow
    {
        public DateOnly? OpeningLatestClosedPeriod { get; init; }
        public DateOnly? ClosingLatestClosedPeriod { get; init; }
    }

    private sealed class StateRow
    {
        public Guid AccountId { get; init; }
        public string AccountCode { get; init; } = null!;
        public string AccountName { get; init; } = null!;
        public StatementSection StatementSection { get; init; }
        public decimal ClosingBalance { get; init; }
    }

    public async Task<StatementOfChangesInEquitySnapshot> GetAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct = default)
    {
        if (toInclusive < fromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(toInclusive), toInclusive, "To must be on or after From.");

        fromInclusive.EnsureMonthStart(nameof(fromInclusive));
        toInclusive.EnsureMonthStart(nameof(toInclusive));

        var openingAsOfPeriod = fromInclusive.AddMonths(-1);
        var latestClosed = await GetLatestClosedPeriodsAsync(openingAsOfPeriod, toInclusive, ct);

        var openingRows = await LoadStateRowsAsync(openingAsOfPeriod, latestClosed.OpeningLatestClosedPeriod, ct);
        var closingRows = await LoadStateRowsAsync(toInclusive, latestClosed.ClosingLatestClosedPeriod, ct);

        var combined = new Dictionary<Guid, MutableRow>();

        foreach (var row in openingRows)
        {
            if (!combined.TryGetValue(row.AccountId, out var current))
            {
                current = new MutableRow(row.AccountId, row.AccountCode, row.AccountName, row.StatementSection);
                combined[row.AccountId] = current;
            }

            current.OpeningBalance += row.ClosingBalance;
        }

        foreach (var row in closingRows)
        {
            if (!combined.TryGetValue(row.AccountId, out var current))
            {
                current = new MutableRow(row.AccountId, row.AccountCode, row.AccountName, row.StatementSection);
                combined[row.AccountId] = current;
            }

            current.ClosingBalance += row.ClosingBalance;
        }

        return new StatementOfChangesInEquitySnapshot(
            Rows: combined.Values
                .OrderBy(x => x.AccountCode, StringComparer.Ordinal)
                .ThenBy(x => x.AccountName, StringComparer.Ordinal)
                .Select(x => new StatementOfChangesInEquitySnapshotRow(
                    x.AccountId,
                    x.AccountCode,
                    x.AccountName,
                    x.StatementSection,
                    x.OpeningBalance,
                    x.ClosingBalance))
                .ToList(),
            OpeningLatestClosedPeriod: latestClosed.OpeningLatestClosedPeriod,
            OpeningRollForwardPeriods: latestClosed.OpeningLatestClosedPeriod is null
                ? 0
                : CountPeriods(latestClosed.OpeningLatestClosedPeriod.Value.AddMonths(1), openingAsOfPeriod),
            ClosingLatestClosedPeriod: latestClosed.ClosingLatestClosedPeriod,
            ClosingRollForwardPeriods: latestClosed.ClosingLatestClosedPeriod is null
                ? 0
                : CountPeriods(latestClosed.ClosingLatestClosedPeriod.Value.AddMonths(1), toInclusive));
    }

    private async Task<LatestClosedPeriodsRow> GetLatestClosedPeriodsAsync(
        DateOnly openingAsOfPeriod,
        DateOnly closingAsOfPeriod,
        CancellationToken ct)
    {
        const string sql = """
                           SELECT
                               MAX(period) FILTER (WHERE period <= @OpeningAsOfPeriod::date) AS OpeningLatestClosedPeriod,
                               MAX(period) FILTER (WHERE period <= @ClosingAsOfPeriod::date) AS ClosingLatestClosedPeriod
                           FROM accounting_closed_periods;
                           """;

        await uow.EnsureConnectionOpenAsync(ct);

        return (await uow.Connection.QuerySingleAsync<LatestClosedPeriodsRow>(
            new CommandDefinition(
                sql,
                new
                {
                    OpeningAsOfPeriod = openingAsOfPeriod,
                    ClosingAsOfPeriod = closingAsOfPeriod
                },
                transaction: uow.Transaction,
                cancellationToken: ct)))!;
    }

    private Task<IReadOnlyList<StateRow>> LoadStateRowsAsync(
        DateOnly asOfPeriod,
        DateOnly? latestClosedPeriod,
        CancellationToken ct)
        => latestClosedPeriod switch
        {
            null => LoadInceptionToDateRowsAsync(asOfPeriod, ct),
            { } snapshotPeriod when snapshotPeriod == asOfPeriod => LoadSnapshotOnlyRowsAsync(snapshotPeriod, ct),
            { } snapshotPeriod => LoadSnapshotPlusDeltaRowsAsync(snapshotPeriod, asOfPeriod, ct)
        };

    private async Task<IReadOnlyList<StateRow>> LoadInceptionToDateRowsAsync(
        DateOnly asOfPeriod,
        CancellationToken ct)
    {
        var sql = """
                  SELECT
                      t.account_id AS AccountId,
                      a.code AS AccountCode,
                      a.name AS AccountName,
                      a.statement_section AS StatementSection,
                      SUM(t.debit_amount - t.credit_amount) AS ClosingBalance
                  FROM accounting_turnovers t
                  JOIN accounting_accounts a
                    ON a.account_id = t.account_id
                   AND a.is_deleted = FALSE
                  WHERE t.period <= @AsOfPeriod::date
                    AND a.statement_section = ANY(@RelevantSections::smallint[])
                  GROUP BY t.account_id, a.code, a.name, a.statement_section
                  ORDER BY a.code;
                  """;

        return await QueryRowsAsync(
            sql,
            new
            {
                AsOfPeriod = asOfPeriod,
                RelevantSections
            },
            ct);
    }

    private async Task<IReadOnlyList<StateRow>> LoadSnapshotOnlyRowsAsync(
        DateOnly snapshotPeriod,
        CancellationToken ct)
    {
        var sql = """
                  SELECT
                      b.account_id AS AccountId,
                      a.code AS AccountCode,
                      a.name AS AccountName,
                      a.statement_section AS StatementSection,
                      SUM(b.closing_balance) AS ClosingBalance
                  FROM accounting_balances b
                  JOIN accounting_accounts a
                    ON a.account_id = b.account_id
                   AND a.is_deleted = FALSE
                  WHERE b.period = @SnapshotPeriod::date
                    AND a.statement_section = ANY(@RelevantSections::smallint[])
                  GROUP BY b.account_id, a.code, a.name, a.statement_section
                  ORDER BY a.code;
                  """;

        return await QueryRowsAsync(
            sql,
            new
            {
                SnapshotPeriod = snapshotPeriod,
                RelevantSections
            },
            ct);
    }

    private async Task<IReadOnlyList<StateRow>> LoadSnapshotPlusDeltaRowsAsync(
        DateOnly snapshotPeriod,
        DateOnly asOfPeriod,
        CancellationToken ct)
    {
        var sql = """
                  WITH snapshot_rows AS (
                      SELECT
                          b.account_id AS AccountId,
                          SUM(b.closing_balance) AS ClosingBalance
                      FROM accounting_balances b
                      JOIN accounting_accounts a
                        ON a.account_id = b.account_id
                       AND a.is_deleted = FALSE
                      WHERE b.period = @SnapshotPeriod::date
                        AND a.statement_section = ANY(@RelevantSections::smallint[])
                      GROUP BY b.account_id
                  ),
                  delta_rows AS (
                      SELECT
                          t.account_id AS AccountId,
                          SUM(t.debit_amount - t.credit_amount) AS ClosingBalance
                      FROM accounting_turnovers t
                      JOIN accounting_accounts a
                        ON a.account_id = t.account_id
                       AND a.is_deleted = FALSE
                      WHERE t.period > @SnapshotPeriod::date
                        AND t.period <= @AsOfPeriod::date
                        AND a.statement_section = ANY(@RelevantSections::smallint[])
                      GROUP BY t.account_id
                  ),
                  final_rows AS (
                      SELECT
                          combined.AccountId,
                          SUM(combined.ClosingBalance) AS ClosingBalance
                      FROM (
                          SELECT AccountId, ClosingBalance FROM snapshot_rows
                          UNION ALL
                          SELECT AccountId, ClosingBalance FROM delta_rows
                      ) combined
                      GROUP BY combined.AccountId
                  )
                  SELECT
                      fr.AccountId,
                      a.code AS AccountCode,
                      a.name AS AccountName,
                      a.statement_section AS StatementSection,
                      fr.ClosingBalance
                  FROM final_rows fr
                  JOIN accounting_accounts a
                    ON a.account_id = fr.AccountId
                   AND a.is_deleted = FALSE
                  ORDER BY a.code;
                  """;

        return await QueryRowsAsync(
            sql,
            new
            {
                SnapshotPeriod = snapshotPeriod,
                AsOfPeriod = asOfPeriod,
                RelevantSections
            },
            ct);
    }

    private async Task<IReadOnlyList<StateRow>> QueryRowsAsync(string sql, object args, CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        return (await uow.Connection.QueryAsync<StateRow>(
            new CommandDefinition(
                sql,
                args,
                transaction: uow.Transaction,
                cancellationToken: ct))).AsList();
    }

    private static int CountPeriods(DateOnly fromInclusive, DateOnly toInclusive)
    {
        if (fromInclusive > toInclusive)
            return 0;

        return (toInclusive.Year - fromInclusive.Year) * 12 + toInclusive.Month - fromInclusive.Month + 1;
    }

    private sealed class MutableRow(Guid accountId, string accountCode, string accountName, StatementSection statementSection)
    {
        public Guid AccountId { get; } = accountId;
        public string AccountCode { get; } = accountCode;
        public string AccountName { get; } = accountName;
        public StatementSection StatementSection { get; } = statementSection;
        public decimal OpeningBalance { get; set; }
        public decimal ClosingBalance { get; set; }
    }
}
