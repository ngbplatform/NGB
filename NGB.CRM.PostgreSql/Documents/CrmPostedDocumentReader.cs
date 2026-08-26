using Dapper;
using NGB.CRM.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.CRM.PostgreSql.Documents;

public sealed class CrmPostedDocumentReader(IUnitOfWork uow) : ICrmPostedDocumentReader
{
    public async Task<IReadOnlyList<Guid>> GetIdsPageAfterAsync(
        string documentType,
        Guid? afterId,
        int limit,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            throw new NgbArgumentRequiredException(nameof(documentType));

        if (limit is <= 0 or > 1_000)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Argument is out of range.");

        await uow.EnsureConnectionOpenAsync(ct);

        var sql = afterId.HasValue
            ? """
              SELECT id
                FROM documents
               WHERE type_code = @DocumentType
                 AND status = 2
                 AND id > @AfterId
               ORDER BY id
               LIMIT @Limit;
              """
            : """
              SELECT id
                FROM documents
               WHERE type_code = @DocumentType
                 AND status = 2
               ORDER BY id
               LIMIT @Limit;
              """;

        var rows = await uow.Connection.QueryAsync<Guid>(new CommandDefinition(
            sql,
            new { DocumentType = documentType.Trim(), AfterId = afterId, Limit = limit },
            transaction: uow.Transaction,
            cancellationToken: ct));

        return rows.AsList();
    }
}
