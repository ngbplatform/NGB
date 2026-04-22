using Dapper;
using NGB.Accounting.Turnovers;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresAccountingTurnoverAggregationReader(IUnitOfWork uow) : IAccountingTurnoverAggregationReader
{
    public async Task<IReadOnlyList<AccountingTurnover>> GetAggregatedFromRegisterAsync(
        DateOnly period,
        CancellationToken ct = default)
    {
        const string sql = """
                           WITH reg AS (
                               SELECT
                                   period_month AS period,
                                   debit_account_id AS account_id,
                                   debit_dimension_set_id AS dimension_set_id,
                                   amount AS debit_amount,
                                   0::numeric AS credit_amount
                               FROM accounting_register_main
                               WHERE period_month = @Period

                               UNION ALL

                               SELECT
                                   period_month AS period,
                                   credit_account_id AS account_id,
                                   credit_dimension_set_id AS dimension_set_id,
                                   0::numeric AS debit_amount,
                                   amount AS credit_amount
                               FROM accounting_register_main
                               WHERE period_month = @Period
                           )
                           SELECT
                               r.period AS Period,
                               r.account_id AS AccountId,
                               r.dimension_set_id AS DimensionSetId,
                               a.code AS AccountCode,
                               SUM(r.debit_amount) AS DebitAmount,
                               SUM(r.credit_amount) AS CreditAmount
                           FROM reg r
                           JOIN accounting_accounts a ON a.account_id = r.account_id AND a.is_deleted = FALSE
                           GROUP BY r.period, r.account_id, r.dimension_set_id, a.code
                           ORDER BY r.period, r.account_id, r.dimension_set_id;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { Period = period },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.EnsureConnectionOpenAsync(ct);
        var rows = await uow.Connection.QueryAsync<AccountingTurnover>(cmd);
        return rows.AsList();
    }

    // Backward-compat helper: some maintenance tools used range aggregation.
    public async Task<IReadOnlyList<AccountingTurnover>> GetAggregatedFromRegisterRangeAsync(
        DateOnly fromPeriod,
        DateOnly toPeriod,
        CancellationToken ct = default)
    {
        const string sql = """
                           WITH reg AS (
                               SELECT
                                   period_month AS period,
                                   debit_account_id AS account_id,
                                   debit_dimension_set_id AS dimension_set_id,
                                   amount AS debit_amount,
                                   0::numeric AS credit_amount
                               FROM accounting_register_main
                               WHERE period_month BETWEEN @From AND @To

                               UNION ALL

                               SELECT
                                   period_month AS period,
                                   credit_account_id AS account_id,
                                   credit_dimension_set_id AS dimension_set_id,
                                   0::numeric AS debit_amount,
                                   amount AS credit_amount
                               FROM accounting_register_main
                               WHERE period_month BETWEEN @From AND @To
                           )
                           SELECT
                               r.period AS Period,
                               r.account_id AS AccountId,
                               r.dimension_set_id AS DimensionSetId,
                               a.code AS AccountCode,
                               SUM(r.debit_amount) AS DebitAmount,
                               SUM(r.credit_amount) AS CreditAmount
                           FROM reg r
                           JOIN accounting_accounts a ON a.account_id = r.account_id AND a.is_deleted = FALSE
                           GROUP BY r.period, r.account_id, r.dimension_set_id, a.code
                           ORDER BY r.period, r.account_id, r.dimension_set_id;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { From = fromPeriod, To = toPeriod },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.EnsureConnectionOpenAsync(ct);
        var rows = await uow.Connection.QueryAsync<AccountingTurnover>(cmd);
        return rows.AsList();
    }
}
