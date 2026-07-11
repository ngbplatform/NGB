using Dapper;
using NGB.CRM.Documents;
using NGB.Persistence.UnitOfWork;

namespace NGB.CRM.PostgreSql.Documents;

public sealed class CrmDocumentReaders(IUnitOfWork uow) : ICrmDocumentReaders
{
    public async Task<CrmLeadIntakeHead> ReadLeadIntakeHeadAsync(Guid documentId, CancellationToken ct = default)
        => await QuerySingleAsync<CrmLeadIntakeHead>(
            """
            SELECT
                document_id AS DocumentId,
                document_date_utc AS DocumentDateUtc,
                lead_name AS LeadName,
                company_name AS CompanyName,
                contact_name AS ContactName,
                email AS Email,
                phone AS Phone,
                lead_source AS LeadSource,
                industry AS Industry,
                estimated_value AS EstimatedValue,
                currency AS Currency,
                notes AS Notes
            FROM doc_crm_lead_intake
            WHERE document_id = @document_id;
            """,
            documentId,
            ct);

    public async Task<CrmLeadQualificationHead> ReadLeadQualificationHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
        => await QuerySingleAsync<CrmLeadQualificationHead>(
            """
            SELECT
                document_id AS DocumentId,
                document_date_utc AS DocumentDateUtc,
                lead_intake_id AS LeadIntakeId,
                qualification_state AS QualificationState,
                score AS Score,
                disqualification_reason AS DisqualificationReason,
                notes AS Notes
            FROM doc_crm_lead_qualification
            WHERE document_id = @document_id;
            """,
            documentId,
            ct);

    public async Task<CrmLeadConversionHead> ReadLeadConversionHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
        => await QuerySingleAsync<CrmLeadConversionHead>(
            """
            SELECT
                document_id AS DocumentId,
                document_date_utc AS DocumentDateUtc,
                lead_intake_id AS LeadIntakeId,
                account_id AS AccountId,
                contact_id AS ContactId,
                create_opportunity AS CreateOpportunity,
                opportunity_name AS OpportunityName,
                stage_id AS StageId,
                amount AS Amount,
                probability AS Probability,
                expected_close_date AS ExpectedCloseDate,
                currency AS Currency,
                notes AS Notes
            FROM doc_crm_lead_conversion
            WHERE document_id = @document_id;
            """,
            documentId,
            ct);

    public async Task<CrmOpportunityUpdateHead> ReadOpportunityUpdateHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
        => await QuerySingleAsync<CrmOpportunityUpdateHead>(
            """
            SELECT
                document_id AS DocumentId,
                document_date_utc AS DocumentDateUtc,
                opportunity_id AS OpportunityId,
                stage_id AS StageId,
                amount AS Amount,
                probability AS Probability,
                expected_close_date AS ExpectedCloseDate,
                status AS Status,
                loss_reason AS LossReason,
                notes AS Notes
            FROM doc_crm_opportunity_update
            WHERE document_id = @document_id;
            """,
            documentId,
            ct);

    public async Task<CrmQuoteHead> ReadQuoteHeadAsync(Guid documentId, CancellationToken ct = default)
        => await QuerySingleAsync<CrmQuoteHead>(
            """
            SELECT
                document_id AS DocumentId,
                document_date_utc AS DocumentDateUtc,
                opportunity_id AS OpportunityId,
                account_id AS AccountId,
                contact_id AS ContactId,
                valid_until AS ValidUntil,
                currency AS Currency,
                quote_status AS QuoteStatus,
                amount AS Amount,
                notes AS Notes
            FROM doc_crm_quote
            WHERE document_id = @document_id;
            """,
            documentId,
            ct);

    public async Task<IReadOnlyList<CrmQuoteLine>> ReadQuoteLinesAsync(Guid documentId, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               document_id AS DocumentId,
                               ordinal AS Ordinal,
                               product_id AS ProductId,
                               description AS Description,
                               quantity AS Quantity,
                               unit_price AS UnitPrice,
                               discount_percent AS DiscountPercent,
                               line_amount AS LineAmount
                           FROM doc_crm_quote__lines
                           WHERE document_id = @document_id
                           ORDER BY ordinal;
                           """;

        var rows = await uow.Connection.QueryAsync<CrmQuoteLine>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));

        return rows.ToArray();
    }

    public async Task<CrmActivityLogHead> ReadActivityLogHeadAsync(Guid documentId, CancellationToken ct = default)
        => await QuerySingleAsync<CrmActivityLogHead>(
            """
            SELECT
                document_id AS DocumentId,
                document_date_utc AS DocumentDateUtc,
                activity_type AS ActivityType,
                subject AS Subject,
                lead_intake_id AS LeadIntakeId,
                account_id AS AccountId,
                contact_id AS ContactId,
                opportunity_id AS OpportunityId,
                due_at_utc AS DueAtUtc,
                completed_at_utc AS CompletedAtUtc,
                outcome AS Outcome,
                notes AS Notes
            FROM doc_crm_activity_log
            WHERE document_id = @document_id;
            """,
            documentId,
            ct);

    private async Task<T> QuerySingleAsync<T>(string sql, Guid documentId, CancellationToken ct)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        return await uow.Connection.QuerySingleAsync<T>(
            new CommandDefinition(sql, new { document_id = documentId }, uow.Transaction, cancellationToken: ct));
    }
}
