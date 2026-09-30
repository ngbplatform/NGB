using Dapper;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.PostgreSql.UnitOfWork;

namespace NGB.PostgreSql.Writers;

/// <summary>
/// Keeps the complete balance projection in PostgreSQL and transfers only bounded policy diagnostics.
/// </summary>
public sealed class PostgresAccountingBalanceProjectionWriter(IUnitOfWork uow) : IAccountingBalanceProjectionWriter
{
    private const int MaxViolationSamples = 100;

    private sealed class ViolationCounts
    {
        public long ForbiddenCount { get; init; }
        public long WarningCount { get; init; }
    }

    public async Task<AccountingBalanceProjectionResult> ProjectAsync(
        DateOnly period,
        bool replaceExisting,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string buildCandidatesSql = """
            CREATE TEMP TABLE IF NOT EXISTS ngb_balance_projection_candidate (
                period DATE NOT NULL,
                account_id UUID NOT NULL,
                dimension_set_id UUID NOT NULL,
                opening_balance NUMERIC NOT NULL,
                closing_balance NUMERIC NOT NULL,
                PRIMARY KEY (account_id, dimension_set_id)
            ) ON COMMIT DROP;

            TRUNCATE ngb_balance_projection_candidate;

            INSERT INTO ngb_balance_projection_candidate
                (period, account_id, dimension_set_id, opening_balance, closing_balance)
            SELECT
                @Period,
                keys.account_id,
                keys.dimension_set_id,
                COALESCE(previous.closing_balance, 0::numeric) AS opening_balance,
                COALESCE(previous.closing_balance, 0::numeric)
                    + COALESCE(current.debit_amount, 0::numeric)
                    - COALESCE(current.credit_amount, 0::numeric) AS closing_balance
            FROM (
                SELECT account_id, dimension_set_id
                FROM accounting_balances
                WHERE period = (@Period::date - INTERVAL '1 month')::date
                UNION
                SELECT account_id, dimension_set_id
                FROM accounting_turnovers
                WHERE period = @Period
            ) keys
            LEFT JOIN accounting_balances previous
              ON previous.period = (@Period::date - INTERVAL '1 month')::date
             AND previous.account_id = keys.account_id
             AND previous.dimension_set_id = keys.dimension_set_id
            LEFT JOIN accounting_turnovers current
              ON current.period = @Period
             AND current.account_id = keys.account_id
             AND current.dimension_set_id = keys.dimension_set_id;
            """;

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            buildCandidatesSql,
            new { Period = period },
            transaction: uow.Transaction,
            cancellationToken: ct));

        const string violationsSql = """
            SELECT
                COUNT(*) FILTER (WHERE a.negative_balance_policy = @ForbidPolicy) AS "ForbiddenCount",
                COUNT(*) FILTER (WHERE a.negative_balance_policy = @WarnPolicy) AS "WarningCount"
            FROM ngb_balance_projection_candidate c
            JOIN accounting_accounts a ON a.account_id = c.account_id
            WHERE a.negative_balance_policy <> @AllowPolicy
              AND (((a.statement_section IN (1, 5, 6, 8)) <> a.is_contra) AND c.closing_balance < 0::numeric
                OR (NOT ((a.statement_section IN (1, 5, 6, 8)) <> a.is_contra)) AND c.closing_balance > 0::numeric);

            SELECT
                c.period AS "Period",
                c.account_id AS "AccountId",
                a.code AS "AccountCode",
                a.name AS "AccountName",
                a.account_type AS "AccountType",
                a.negative_balance_policy AS "Policy",
                c.dimension_set_id AS "DimensionSetId",
                c.closing_balance AS "ClosingBalance"
            FROM ngb_balance_projection_candidate c
            JOIN accounting_accounts a ON a.account_id = c.account_id
            WHERE a.negative_balance_policy <> @AllowPolicy
              AND (((a.statement_section IN (1, 5, 6, 8)) <> a.is_contra) AND c.closing_balance < 0::numeric
                OR (NOT ((a.statement_section IN (1, 5, 6, 8)) <> a.is_contra)) AND c.closing_balance > 0::numeric)
            ORDER BY a.negative_balance_policy DESC, a.code, c.dimension_set_id
            LIMIT @MaxViolationSamples;
            """;

        ViolationCounts counts;
        IReadOnlyList<NegativeBalanceViolation> samples;
        await using (var grid = await uow.Connection.QueryMultipleAsync(new CommandDefinition(
                         violationsSql,
                         new
                         {
                             AllowPolicy = (short)NegativeBalancePolicy.Allow,
                             WarnPolicy = (short)NegativeBalancePolicy.Warn,
                             ForbidPolicy = (short)NegativeBalancePolicy.Forbid,
                             MaxViolationSamples
                         },
                         transaction: uow.Transaction,
                         cancellationToken: ct)))
        {
            counts = await grid.ReadSingleAsync<ViolationCounts>();
            samples = (await grid.ReadAsync<NegativeBalanceViolation>()).AsList();
        }

        if (counts.ForbiddenCount > 0)
        {
            return new AccountingBalanceProjectionResult(
                RowsWritten: 0,
                counts.ForbiddenCount,
                counts.WarningCount,
                samples);
        }

        var writeSql = replaceExisting
            ? """
              DELETE FROM accounting_balances WHERE period = @Period;
              INSERT INTO accounting_balances
                  (period, account_id, dimension_set_id, opening_balance, closing_balance)
              SELECT period, account_id, dimension_set_id, opening_balance, closing_balance
              FROM ngb_balance_projection_candidate
              ORDER BY account_id, dimension_set_id;
              """
            : """
              INSERT INTO accounting_balances
                  (period, account_id, dimension_set_id, opening_balance, closing_balance)
              SELECT period, account_id, dimension_set_id, opening_balance, closing_balance
              FROM ngb_balance_projection_candidate
              ON CONFLICT (period, account_id, dimension_set_id)
              DO UPDATE SET
                  opening_balance = EXCLUDED.opening_balance,
                  closing_balance = EXCLUDED.closing_balance;
              """;

        var rowsWritten = await uow.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            writeSql + Environment.NewLine + "SELECT COUNT(*)::int FROM ngb_balance_projection_candidate;",
            new { Period = period },
            transaction: uow.Transaction,
            cancellationToken: ct));

        return new AccountingBalanceProjectionResult(
            rowsWritten,
            counts.ForbiddenCount,
            counts.WarningCount,
            samples);
    }
}
