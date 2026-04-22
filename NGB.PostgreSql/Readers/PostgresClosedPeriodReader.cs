using Dapper;
using NGB.Accounting.Periods;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresClosedPeriodReader(IUnitOfWork uow) : IClosedPeriodReader
{
    public async Task<IReadOnlyList<ClosedPeriodRecord>> GetClosedAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        
        fromInclusive.EnsureMonthStart(nameof(fromInclusive));
        toInclusive.EnsureMonthStart(nameof(toInclusive));

        // Defensive behavior:
        // Readers are frequently used by UX/reporting layers that may occasionally build an empty range
        // (e.g. requesting "prior months" when the current period is January).
        // Returning an empty list is safer than throwing and potentially crashing background services.
        if (toInclusive < fromInclusive)
            return [];

        const string sql = """
                           SELECT period       AS Period,
                                  closed_by    AS ClosedBy,
                                  closed_at_utc AS ClosedAtUtc
                           FROM accounting_closed_periods
                           WHERE period BETWEEN @fromInclusive AND @toInclusive
                           ORDER BY period;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { fromInclusive, toInclusive },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync<ClosedPeriodRecord>(cmd);
        return rows.AsList();
    }

    public async Task<DateOnly?> GetLatestClosedPeriodAsync(CancellationToken ct = default)
    {
        const string sql = """
                           SELECT MAX(period)
                           FROM accounting_closed_periods;
                           """;

        await uow.EnsureConnectionOpenAsync(ct);

        return await uow.Connection.ExecuteScalarAsync<DateOnly?>(
            new CommandDefinition(
                sql,
                transaction: uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<bool> ExistsClosedAfterAsync(DateOnly period, CancellationToken ct = default)
    {
        const string sql = """
                           SELECT EXISTS (
                               SELECT 1
                               FROM accounting_closed_periods
                               WHERE period > @Period
                           );
                           """;

        await uow.EnsureConnectionOpenAsync(ct);

        return await uow.Connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new { Period = period },
                transaction: uow.Transaction,
                cancellationToken: ct));
    }
}
