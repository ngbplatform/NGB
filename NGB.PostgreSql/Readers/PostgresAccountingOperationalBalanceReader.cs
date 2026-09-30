using Dapper;
using NGB.Accounting.Balances;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresAccountingOperationalBalanceReader(IUnitOfWork uow) : IAccountingOperationalBalanceReader
{
    public async Task<IReadOnlyList<AccountingOperationalBalanceSnapshot>> GetForKeysAsync(
        DateOnly period,
        IReadOnlyList<AccountingBalanceKey> keys,
        CancellationToken ct = default)
    {
        if (keys.Count == 0)
            return [];

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           WITH previous_period AS (
                               SELECT MAX(period) AS period
                               FROM accounting_closed_periods
                               WHERE period <= @Period
                           ),
                           k AS (
                               SELECT *
                               FROM UNNEST(
                                   @AccountIds::uuid[],
                                   @DimensionSetIds::uuid[]
                               ) AS k(account_id, dimension_set_id)
                           )
                           SELECT
                               @Period AS Period,
                               k.account_id AS AccountId,
                               k.dimension_set_id AS DimensionSetId,
                               COALESCE(pb.closing_balance, 0) AS PreviousClosingBalance,
                               COALESCE(t.debit_amount, 0) AS DebitTurnover,
                               COALESCE(t.credit_amount, 0) AS CreditTurnover
                           FROM k
                           CROSS JOIN previous_period pp
                           LEFT JOIN accounting_balances pb
                               ON pb.period = pp.period
                              AND pb.account_id = k.account_id
                              AND pb.dimension_set_id = k.dimension_set_id
                           LEFT JOIN accounting_turnovers t
                               ON t.period = @Period
                              AND t.account_id = k.account_id
                              AND t.dimension_set_id = k.dimension_set_id
                           ORDER BY k.account_id, k.dimension_set_id;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                AccountIds = keys.Select(x => x.AccountId).ToArray(),
                DimensionSetIds = keys.Select(x => x.DimensionSetId).ToArray(),
                Period = period
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync<AccountingOperationalBalanceSnapshot>(cmd);
        return rows.ToList();
    }
}
