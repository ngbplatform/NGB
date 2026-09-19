using Dapper;
using NGB.Persistence.Reporting;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Reporting;

public sealed class PostgresReportReadSession(IUnitOfWork uow) : IReportReadSession
{
    private bool _ownsTransaction;
    public Guid SnapshotId { get; private set; }

    public async Task BeginAsync(CancellationToken ct)
    {
        if (uow.HasActiveTransaction)
            throw new InvalidOperationException("A report read session requires its own transaction.");

        await uow.BeginTransactionAsync(ct);

        _ownsTransaction = true;

        try
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                "SET TRANSACTION ISOLATION LEVEL REPEATABLE READ, READ ONLY",
                transaction: uow.Transaction,
                cancellationToken: ct));
            SnapshotId = Guid.NewGuid();
        }
        catch
        {
            await EndAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task EndAsync(CancellationToken ct)
    {
        if (!_ownsTransaction)
            return;

        _ownsTransaction = false;
        SnapshotId = Guid.Empty;

        await uow.RollbackAsync(ct);
    }
}
