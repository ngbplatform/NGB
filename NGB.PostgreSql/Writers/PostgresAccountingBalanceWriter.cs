using Dapper;
using NGB.Accounting.Balances;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.PostgreSql.UnitOfWork;

namespace NGB.PostgreSql.Writers;

public sealed class PostgresAccountingBalanceWriter(IUnitOfWork uow) : IAccountingBalanceWriter
{
    // Prevents huge parameter arrays / oversized messages for very large periods.
    // Tune based on profiling; 5k rows is a safe default for UNNEST UPSERTs.
    private const int MaxBatchSize = 5_000;

    public async Task DeleteForPeriodAsync(DateOnly period, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        const string sql = "DELETE FROM accounting_balances WHERE period = @Period;";

        var cmd = new CommandDefinition(
            sql,
            new { Period = period },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.EnsureConnectionOpenAsync(ct);
        await uow.Connection.ExecuteAsync(cmd);
    }

    public async Task SaveAsync(IEnumerable<AccountingBalance> balances, CancellationToken ct = default)
    {
        // Keep method name aligned with interface.
        await WriteAsync(balances, ct);
    }

    // Backward-compat helper: some internal maintenance tools previously used "WriteAsync" naming.
    public async Task WriteAsync(IEnumerable<AccountingBalance> balances, CancellationToken ct = default)
    {
        var list = balances as IList<AccountingBalance> ?? balances.ToList();
        if (list.Count == 0)
            return;

        await uow.EnsureOpenForTransactionAsync(ct);

        for (var offset = 0; offset < list.Count; offset += MaxBatchSize)
        {
            var count = Math.Min(MaxBatchSize, list.Count - offset);
            await WriteBatchAsync(list, offset, count, ct);
        }
    }

    private static readonly string UpsertSql = """
            INSERT INTO accounting_balances
            (period, account_id, dimension_set_id, opening_balance, closing_balance)
            SELECT *
            FROM UNNEST(
                @Periods::date[],
                @AccountIds::uuid[],
                @DimensionSetIds::uuid[],
                @OpeningBalances::numeric[],
                @ClosingBalances::numeric[]
            ) AS t(period, account_id, dimension_set_id, opening_balance, closing_balance)
            ON CONFLICT (period, account_id, dimension_set_id)
            DO UPDATE SET
                opening_balance = EXCLUDED.opening_balance,
                closing_balance = EXCLUDED.closing_balance;
            """;

    private async Task WriteBatchAsync(IList<AccountingBalance> list, int offset, int count, CancellationToken ct)
    {
        var periods = new DateOnly[count];
        var accountIds = new Guid[count];
        var dimensionSetIds = new Guid[count];
        var opening = new decimal[count];
        var closing = new decimal[count];

        for (var i = 0; i < count; i++)
        {
            var b = list[offset + i];
            periods[i] = b.Period;
            accountIds[i] = b.AccountId;
            dimensionSetIds[i] = b.DimensionSetId;
            opening[i] = b.OpeningBalance;
            closing[i] = b.ClosingBalance;
        }

        var cmd = new CommandDefinition(
            UpsertSql,
            new
            {
                Periods = periods,
                AccountIds = accountIds,
                DimensionSetIds = dimensionSetIds,
                OpeningBalances = opening,
                ClosingBalances = closing
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }
}
