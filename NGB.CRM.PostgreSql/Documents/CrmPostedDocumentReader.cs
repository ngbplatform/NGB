using Dapper;
using NGB.CRM.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.CRM.PostgreSql.Documents;

public sealed class CrmPostedDocumentReader(IUnitOfWork uow) : ICrmPostedDocumentReader
{
    public async Task<IReadOnlyList<Guid>> GetIdsMissingReferenceRegisterPostPageAfterAsync(
        string documentType,
        Guid primaryRegisterId,
        Guid? createOpportunityRegisterId,
        Guid? afterId,
        int limit,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            throw new NgbArgumentRequiredException(nameof(documentType));

        if (primaryRegisterId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(primaryRegisterId));

        if (createOpportunityRegisterId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(createOpportunityRegisterId), "RegisterId must be null or non-empty.");

        if (limit is <= 0 or > 1_000)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Argument is out of range.");

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT document.id
                             FROM documents document
                             LEFT JOIN doc_crm_lead_conversion conversion
                               ON conversion.document_id = document.id
                              AND document.type_code = @LeadConversionType
                            WHERE document.type_code = @DocumentType
                              AND document.status = 2
                              AND (@AfterId::uuid IS NULL OR document.id > @AfterId)
                              AND (
                                  NOT EXISTS (
                                      SELECT 1
                                        FROM reference_register_write_state state
                                       WHERE state.document_id = document.id
                                         AND state.register_id = @PrimaryRegisterId
                                         AND state.operation = 1
                                         AND state.completed_at_utc IS NOT NULL
                                  )
                                  OR (
                                      @CreateOpportunityRegisterId::uuid IS NOT NULL
                                      AND COALESCE(conversion.create_opportunity, FALSE)
                                      AND NOT EXISTS (
                                          SELECT 1
                                            FROM reference_register_write_state state
                                           WHERE state.document_id = document.id
                                             AND state.register_id = @CreateOpportunityRegisterId
                                             AND state.operation = 1
                                             AND state.completed_at_utc IS NOT NULL
                                      )
                                  )
                              )
                            ORDER BY document.id
                            LIMIT @Limit;
                           """;

        var rows = await uow.Connection.QueryAsync<Guid>(new CommandDefinition(
            sql,
            new
            {
                DocumentType = documentType.Trim(),
                LeadConversionType = CrmCodes.LeadConversion,
                PrimaryRegisterId = primaryRegisterId,
                CreateOpportunityRegisterId = createOpportunityRegisterId,
                AfterId = afterId,
                Limit = limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct));

        return rows.AsList();
    }
}
