using Dapper;
using NGB.Accounting.Turnovers;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.PostgreSql.UnitOfWork;

namespace NGB.PostgreSql.Writers;

public sealed class PostgresAccountingTurnoverWriter(IUnitOfWork uow) : IAccountingTurnoverWriter
{
    // Prevents huge parameter arrays / oversized messages for very large periods.
    // Tune based on profiling; 5k rows is a safe default for UNNEST UPSERTs.
    private const int MaxBatchSize = 5_000;

    public async Task DeleteForPeriodAsync(DateOnly period, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        const string sql = "DELETE FROM accounting_turnovers WHERE period = @Period;";

        var cmd = new CommandDefinition(
            sql,
            new { Period = period },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.EnsureConnectionOpenAsync(ct);
        await uow.Connection.ExecuteAsync(cmd);
    }

    public async Task WriteAsync(IEnumerable<AccountingTurnover> turnovers, CancellationToken ct = default)
    {
        var list = turnovers as IList<AccountingTurnover> ?? turnovers.ToList();
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
            INSERT INTO accounting_turnovers
            (period, account_id, dimension_set_id, debit_amount, credit_amount)
            SELECT *
            FROM UNNEST(
                @Periods::date[],
                @AccountIds::uuid[],
                @DimensionSetIds::uuid[],
                @DebitAmounts::numeric[],
                @CreditAmounts::numeric[]
            ) AS t(period, account_id, dimension_set_id, debit_amount, credit_amount)
            ON CONFLICT (period, account_id, dimension_set_id)
            DO UPDATE SET
                debit_amount  = accounting_turnovers.debit_amount  + EXCLUDED.debit_amount,
                credit_amount = accounting_turnovers.credit_amount + EXCLUDED.credit_amount;
            """;

    private async Task WriteBatchAsync(IList<AccountingTurnover> list, int offset, int count, CancellationToken ct)
    {
        // High-throughput UPSERT: a single round-trip via UNNEST instead of N INSERT statements.
        var periods = new DateOnly[count];
        var accountIds = new Guid[count];
        var dimensionSetIds = new Guid[count];
        var debit = new decimal[count];
        var credit = new decimal[count];

        for (var i = 0; i < count; i++)
        {
            var t = list[offset + i];
            periods[i] = t.Period;
            accountIds[i] = t.AccountId;
            dimensionSetIds[i] = t.DimensionSetId;
            debit[i] = t.DebitAmount;
            credit[i] = t.CreditAmount;
        }

        var cmd = new CommandDefinition(
            UpsertSql,
            new
            {
                Periods = periods,
                AccountIds = accountIds,
                DimensionSetIds = dimensionSetIds,
                DebitAmounts = debit,
                CreditAmounts = credit
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }
}
