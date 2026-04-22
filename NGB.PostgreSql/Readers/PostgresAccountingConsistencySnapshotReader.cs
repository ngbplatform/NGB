using Dapper;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Readers;

/// <summary>
/// PostgreSQL reader for Accounting Consistency snapshots.
/// Returns flat rows combining current balances, current turnovers, and optional previous-period balances.
/// </summary>
public sealed class PostgresAccountingConsistencySnapshotReader(IUnitOfWork uow) : IAccountingConsistencySnapshotReader
{
    private sealed class Row
    {
        public Guid AccountId { get; init; }
        public string AccountCode { get; init; } = null!;
        public Guid DimensionSetId { get; init; }
        public decimal OpeningBalance { get; init; }
        public decimal ClosingBalance { get; init; }
        public decimal DebitAmount { get; init; }
        public decimal CreditAmount { get; init; }
        public decimal PreviousClosingBalance { get; init; }
        public bool HasCurrentBalanceRow { get; init; }
        public bool HasTurnoverRow { get; init; }
        public bool HasPreviousBalanceRow { get; init; }
    }

    public async Task<AccountingConsistencySnapshot> GetAsync(
        DateOnly period,
        DateOnly? previousPeriodForChainCheck = null,
        CancellationToken ct = default)
    {
        period.EnsureMonthStart(nameof(period));
        previousPeriodForChainCheck?.EnsureMonthStart(nameof(previousPeriodForChainCheck));

        const string sql = """
                           WITH current_balances AS (
                               SELECT
                                   b.account_id AS account_id,
                                   b.dimension_set_id AS dimension_set_id,
                                   b.opening_balance AS opening_balance,
                                   b.closing_balance AS closing_balance
                               FROM accounting_balances b
                               WHERE b.period = @Period::date
                           ),
                           current_turnovers AS (
                               SELECT
                                   t.account_id AS account_id,
                                   t.dimension_set_id AS dimension_set_id,
                                   t.debit_amount AS debit_amount,
                                   t.credit_amount AS credit_amount
                               FROM accounting_turnovers t
                               WHERE t.period = @Period::date
                           ),
                           previous_balances AS (
                               SELECT
                                   b.account_id AS account_id,
                                   b.dimension_set_id AS dimension_set_id,
                                   b.closing_balance AS previous_closing_balance
                               FROM accounting_balances b
                               WHERE b.period = @PreviousPeriod::date
                           ),
                           current_rows AS (
                               SELECT
                                   COALESCE(b.account_id, t.account_id) AS account_id,
                                   COALESCE(b.dimension_set_id, t.dimension_set_id) AS dimension_set_id,
                                   COALESCE(b.opening_balance, 0::numeric) AS opening_balance,
                                   COALESCE(b.closing_balance, 0::numeric) AS closing_balance,
                                   COALESCE(t.debit_amount, 0::numeric) AS debit_amount,
                                   COALESCE(t.credit_amount, 0::numeric) AS credit_amount,
                                   (b.account_id IS NOT NULL) AS has_current_balance_row,
                                   (t.account_id IS NOT NULL) AS has_turnover_row
                               FROM current_balances b
                               FULL JOIN current_turnovers t
                                 ON t.account_id = b.account_id
                                AND t.dimension_set_id = b.dimension_set_id
                           ),
                           final_rows AS (
                               SELECT
                                   COALESCE(cr.account_id, pb.account_id) AS account_id,
                                   COALESCE(cr.dimension_set_id, pb.dimension_set_id) AS dimension_set_id,
                                   COALESCE(cr.opening_balance, 0::numeric) AS opening_balance,
                                   COALESCE(cr.closing_balance, 0::numeric) AS closing_balance,
                                   COALESCE(cr.debit_amount, 0::numeric) AS debit_amount,
                                   COALESCE(cr.credit_amount, 0::numeric) AS credit_amount,
                                   COALESCE(pb.previous_closing_balance, 0::numeric) AS previous_closing_balance,
                                   COALESCE(cr.has_current_balance_row, FALSE) AS has_current_balance_row,
                                   COALESCE(cr.has_turnover_row, FALSE) AS has_turnover_row,
                                   (pb.account_id IS NOT NULL) AS has_previous_balance_row
                               FROM current_rows cr
                               FULL JOIN previous_balances pb
                                 ON pb.account_id = cr.account_id
                                AND pb.dimension_set_id = cr.dimension_set_id
                           )
                           SELECT
                               fr.account_id AS AccountId,
                               a.code AS AccountCode,
                               fr.dimension_set_id AS DimensionSetId,
                               fr.opening_balance AS OpeningBalance,
                               fr.closing_balance AS ClosingBalance,
                               fr.debit_amount AS DebitAmount,
                               fr.credit_amount AS CreditAmount,
                               fr.previous_closing_balance AS PreviousClosingBalance,
                               fr.has_current_balance_row AS HasCurrentBalanceRow,
                               fr.has_turnover_row AS HasTurnoverRow,
                               fr.has_previous_balance_row AS HasPreviousBalanceRow
                           FROM final_rows fr
                           JOIN accounting_accounts a ON a.account_id = fr.account_id AND a.is_deleted = FALSE
                           ORDER BY a.code, fr.dimension_set_id;
                           """;

        await uow.EnsureConnectionOpenAsync(ct);

        var rows = (await uow.Connection.QueryAsync<Row>(
            new CommandDefinition(
                sql,
                new
                {
                    Period = period,
                    PreviousPeriod = previousPeriodForChainCheck
                },
                transaction: uow.Transaction,
                cancellationToken: ct))).AsList();

        return new AccountingConsistencySnapshot(
            rows.Select(x => new AccountingConsistencySnapshotRow(
                    x.AccountId,
                    x.AccountCode,
                    x.DimensionSetId,
                    x.OpeningBalance,
                    x.ClosingBalance,
                    x.DebitAmount,
                    x.CreditAmount,
                    x.PreviousClosingBalance,
                    x.HasCurrentBalanceRow,
                    x.HasTurnoverRow,
                    x.HasPreviousBalanceRow))
                .ToList());
    }
}
