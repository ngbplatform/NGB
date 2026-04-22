using Dapper;
using NGB.Persistence.Checkers;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Checkers;

public sealed class PostgresAccountingIntegrityChecker(IUnitOfWork uow)
    : IAccountingIntegrityDiagnostics, IAccountingIntegrityChecker
{
    public async Task<long> GetTurnoversVsRegisterDiffCountAsync(DateOnly period, CancellationToken ct = default)
    {
        // Ensures no drift between:
        // 1) register_main aggregation
        // 2) stored monthly turnovers
        //
        // Keyed by (period, account_id, dimension_set_id).

        const string sql = """
                           WITH reg AS (
                               SELECT
                                   period_month AS period,
                                   debit_account_id AS account_id,
                                   debit_dimension_set_id AS dimension_set_id,
                                   SUM(amount) AS debit_amount,
                                   0::numeric AS credit_amount
                               FROM accounting_register_main
                               WHERE period_month = @Period
                               GROUP BY 1,2,3

                               UNION ALL

                               SELECT
                                   period_month AS period,
                                   credit_account_id AS account_id,
                                   credit_dimension_set_id AS dimension_set_id,
                                   0::numeric AS debit_amount,
                                   SUM(amount) AS credit_amount
                               FROM accounting_register_main
                               WHERE period_month = @Period
                               GROUP BY 1,2,3
                           ),
                           reg_agg AS (
                               SELECT
                                   period,
                                   account_id,
                                   dimension_set_id,
                                   SUM(debit_amount) AS debit_amount,
                                   SUM(credit_amount) AS credit_amount
                               FROM reg
                               GROUP BY 1,2,3
                           ),
                           stored AS (
                               SELECT
                                   period,
                                   account_id,
                                   dimension_set_id,
                                   debit_amount,
                                   credit_amount
                               FROM accounting_turnovers
                               WHERE period = @Period
                           ),
                           diff AS (
                               SELECT
                                   COALESCE(r.period, s.period) AS period,
                                   COALESCE(r.account_id, s.account_id) AS account_id,
                                   COALESCE(r.dimension_set_id, s.dimension_set_id) AS dimension_set_id,
                                   COALESCE(r.debit_amount, 0) AS reg_debit,
                                   COALESCE(r.credit_amount, 0) AS reg_credit,
                                   COALESCE(s.debit_amount, 0) AS stored_debit,
                                   COALESCE(s.credit_amount, 0) AS stored_credit
                               FROM reg_agg r
                               FULL JOIN stored s
                                   ON s.period = r.period
                                  AND s.account_id = r.account_id
                                  AND s.dimension_set_id = r.dimension_set_id
                           )
                           SELECT COUNT(*)
                           FROM diff
                           WHERE reg_debit <> stored_debit
                              OR reg_credit <> stored_credit;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { Period = period },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.EnsureConnectionOpenAsync(ct);
        return await uow.Connection.ExecuteScalarAsync<long>(cmd);
    }

    public async Task AssertPeriodIsBalancedAsync(DateOnly period, CancellationToken ct = default)
    {
        // Basic sanity:
        // SUM(debit) == SUM(credit) for the period in the register.

        // Also validate derived data integrity:
        // accounting_turnovers must equal aggregation from accounting_register_main.
        // This check is relied upon by month closing and maintenance tests.
        var mismatchedKeys = await GetTurnoversVsRegisterDiffCountAsync(period, ct);
        if (mismatchedKeys > 0)
            throw new NgbInvariantViolationException(
                $"Integrity violation: turnovers mismatch for period {period:yyyy-MM-dd}. Mismatched keys: {mismatchedKeys}.",
                context: new Dictionary<string, object?>
                {
                    ["period"] = period,
                    ["mismatchedKeys"] = mismatchedKeys
                });

        const string sql = """
                           SELECT
                               COALESCE(SUM(amount),0) AS sum_debit
                           FROM accounting_register_main
                           WHERE period_month = @Period;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { Period = period },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.EnsureConnectionOpenAsync(ct);

        var sumDebit = await uow.Connection.ExecuteScalarAsync<decimal>(cmd);

        // For double-entry register_main each row contributes one debit and one credit of the same amount.
        // So sum of amounts equals both total debit and total credit.
        if (sumDebit < 0m)
            throw new NgbInvariantViolationException(
                "Register amount sum is negative; register integrity broken.",
                context: new Dictionary<string, object?>
                {
                    ["period"] = period,
                    ["sumDebit"] = sumDebit
                });
    }
}
