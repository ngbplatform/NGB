using Dapper;
using NGB.Contracts.BusinessObjects;
using NGB.Persistence.Attachments;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Attachments;

internal sealed class PostgresBusinessObjectContentSummaryReader(IUnitOfWork uow) : IBusinessObjectContentSummaryReader
{
    public async Task<BusinessObjectContentSummary> GetAsync(
        BusinessObjectRef target,
        bool attachments,
        bool notes,
        CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        return await uow.Connection.QuerySingleAsync<BusinessObjectContentSummary>(new CommandDefinition(
            """
            SELECT CASE WHEN @Attachments THEN (SELECT count(*) FROM platform_attachments
                WHERE object_kind = @Kind AND object_type_code = @TypeCode AND object_id = @Id AND status = 2) END AS "Attachments",
                CASE WHEN @Notes THEN (SELECT count(*) FROM platform_notes
                WHERE object_kind = @Kind AND object_type_code = @TypeCode AND object_id = @Id AND NOT is_deleted) END AS "Notes";
            """,
            new
            {
                Kind = (short)target.Kind,
                target.TypeCode,
                target.Id,
                Attachments = attachments,
                Notes = notes
            },
            uow.Transaction,
            cancellationToken: ct));
    }
}
