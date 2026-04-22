using Dapper;
using NGB.Persistence.Periods;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Periods;

public sealed class PostgresClosedPeriodRepository(IUnitOfWork uow) : IClosedPeriodRepository
{
    public async Task<bool> IsClosedAsync(DateOnly period, CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        
        const string sql = """
                           SELECT EXISTS (
                               SELECT 1
                               FROM accounting_closed_periods
                               WHERE period = @Period
                           );
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { Period = period },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.ExecuteScalarAsync<bool>(cmd);
    }

    public async Task MarkClosedAsync(
        DateOnly period,
        string closedBy,
        DateTime closedAtUtc,
        CancellationToken ct = default)
    {
        closedAtUtc.EnsureUtc(nameof(closedAtUtc));

        uow.EnsureActiveTransaction();

        // IMPORTANT production behavior:
        // - DO NOT silently ignore duplicates (no ON CONFLICT DO NOTHING).
        // - If period is already closed, this must fail loudly (and roll back the closing transaction),
        //   because double-close is a data race / logic error.
        const string sql = """
                           INSERT INTO accounting_closed_periods(period, closed_by, closed_at_utc)
                           VALUES (@Period, @ClosedBy, @ClosedAtUtc);
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { Period = period, ClosedBy = closedBy, ClosedAtUtc = closedAtUtc },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.EnsureConnectionOpenAsync(ct);
        await uow.Connection.ExecuteAsync(cmd);
    }

    public async Task ReopenAsync(DateOnly period, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        const string sql = """
                           DELETE FROM accounting_closed_periods
                           WHERE period = @Period;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { Period = period },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.EnsureConnectionOpenAsync(ct);
        await uow.Connection.ExecuteAsync(cmd);
    }
}
