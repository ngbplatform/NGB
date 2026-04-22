using Dapper;
using NGB.Accounting.Periods;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresAccountingPeriodActivityReader(IUnitOfWork uow) : IAccountingPeriodActivityReader
{
    public async Task<DateOnly?> GetEarliestActivityPeriodAsync(CancellationToken ct = default)
    {
        const string sql = """
                           SELECT MIN(period_month)
                           FROM accounting_register_main;
                           """;

        await uow.EnsureConnectionOpenAsync(ct);

        return await uow.Connection.ExecuteScalarAsync<DateOnly?>(
            new CommandDefinition(
                sql,
                transaction: uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<DateOnly>> GetActivityPeriodsAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        fromInclusive.EnsureMonthStart(nameof(fromInclusive));
        toInclusive.EnsureMonthStart(nameof(toInclusive));

        if (toInclusive < fromInclusive)
            return [];

        const string sql = """
                           SELECT DISTINCT period_month
                           FROM accounting_register_main
                           WHERE period_month BETWEEN @FromInclusive AND @ToInclusive
                           ORDER BY period_month;
                           """;

        var rows = await uow.Connection.QueryAsync<DateOnly>(
            new CommandDefinition(
                sql,
                new
                {
                    FromInclusive = AccountingPeriod.FromDateOnly(fromInclusive),
                    ToInclusive = AccountingPeriod.FromDateOnly(toInclusive)
                },
                transaction: uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }
}
