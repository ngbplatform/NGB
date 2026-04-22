using Dapper;
using NGB.Accounting.Registers;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.PostgreSql.UnitOfWork;

namespace NGB.PostgreSql.Writers;

public sealed class PostgresAccountingEntryWriter(IUnitOfWork uow) : IAccountingEntryWriter
{
    // Prevents huge parameter arrays / oversized messages for very large postings.
    // Tune based on profiling; 5k rows is a safe default for UNNEST inserts.
    private const int MaxBatchSize = 5_000;

    public async Task WriteAsync(IReadOnlyList<AccountingEntry> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0)
            return;

        // Writers must never run outside an explicit transaction.
        // Otherwise, Dapper will execute commands in autocommit mode, violating platform atomicity.
        await uow.EnsureOpenForTransactionAsync(ct);

        for (var offset = 0; offset < entries.Count; offset += MaxBatchSize)
        {
            var count = Math.Min(MaxBatchSize, entries.Count - offset);
            await WriteBatchAsync(entries, offset, count, ct);
        }
    }

    private static readonly string InsertSql = """
            INSERT INTO accounting_register_main
            (document_id, period,
             debit_account_id, credit_account_id,
             debit_dimension_set_id, credit_dimension_set_id,
             amount, is_storno)
            SELECT *
            FROM UNNEST(
                @DocumentIds::uuid[],
                @Periods::timestamptz[],
                @DebitAccountIds::uuid[],
                @CreditAccountIds::uuid[],
                @DebitDimensionSetIds::uuid[],
                @CreditDimensionSetIds::uuid[],
                @Amounts::numeric[],
                @IsStorno::boolean[]
            );
            """;

    private async Task WriteBatchAsync(
        IReadOnlyList<AccountingEntry> entries,
        int offset,
        int count,
        CancellationToken ct)
    {
        // High-throughput insert: a single round-trip via UNNEST instead of N INSERT statements.
        var documentIds = new Guid[count];
        var periods = new DateTime[count];
        var debitAccountIds = new Guid[count];
        var creditAccountIds = new Guid[count];
        var debitDimensionSetIds = new Guid[count];
        var creditDimensionSetIds = new Guid[count];
        var amounts = new decimal[count];
        var isStorno = new bool[count];

        for (var i = 0; i < count; i++)
        {
            var e = entries[offset + i];

            documentIds[i] = e.DocumentId;
            periods[i] = e.Period;
            debitAccountIds[i] = e.Debit.Id;
            creditAccountIds[i] = e.Credit.Id;

            debitDimensionSetIds[i] = e.DebitDimensionSetId;
            creditDimensionSetIds[i] = e.CreditDimensionSetId;

            amounts[i] = e.Amount;
            isStorno[i] = e.IsStorno;
        }

        var cmd = new CommandDefinition(
            InsertSql,
            new
            {
                DocumentIds = documentIds,
                Periods = periods,
                DebitAccountIds = debitAccountIds,
                CreditAccountIds = creditAccountIds,
                DebitDimensionSetIds = debitDimensionSetIds,
                CreditDimensionSetIds = creditDimensionSetIds,
                Amounts = amounts,
                IsStorno = isStorno
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }
}
