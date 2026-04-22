using Dapper;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.PostgreSql.UnitOfWork;

namespace NGB.PostgreSql.Writers;

public sealed class PostgresAccountingEntryMaintenanceWriter(IUnitOfWork uow) : IAccountingEntryMaintenanceWriter
{
    public async Task<IReadOnlyList<DateOnly>> DeleteByDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           WITH deleted AS (
                               DELETE FROM accounting_register_main
                               WHERE document_id = @DocumentId
                               RETURNING period_month
                           )
                           SELECT DISTINCT period_month
                           FROM deleted
                           ORDER BY period_month;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { DocumentId = documentId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync<DateOnly>(cmd);
        return rows.AsList();
    }
}
