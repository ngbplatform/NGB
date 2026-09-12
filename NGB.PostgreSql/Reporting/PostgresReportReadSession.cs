using Dapper;
using NGB.Persistence.Reporting;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Reporting;

public sealed class PostgresReportReadSession(IUnitOfWork uow) : IReportReadSession
{
    public async Task BeginAsync(CancellationToken ct)
    {
        await uow.BeginTransactionAsync(ct);
        
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "SET TRANSACTION ISOLATION LEVEL REPEATABLE READ, READ ONLY",
            transaction: uow.Transaction,
            cancellationToken: ct));
    }
}
