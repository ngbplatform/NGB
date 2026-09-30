using Dapper;
using NGB.CRM.Reporting;
using NGB.Core.Documents;
using NGB.Persistence.UnitOfWork;

namespace NGB.CRM.PostgreSql.Reporting;

public sealed class PostgresCrmDashboardReader(IUnitOfWork uow) : ICrmDashboardReader
{
    public async Task<CrmDashboardSnapshot> GetAsync(
        DateOnly asOfUtc,
        int opportunityLimit,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(opportunityLimit, 1);
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    COALESCE((SELECT SUM(amount) FROM crm_opportunities_current), 0) AS PipelineAmount,
    COALESCE((SELECT SUM(weighted_amount) FROM crm_opportunities_current), 0) AS WeightedPipelineAmount,
    (SELECT COUNT(*)::integer
       FROM doc_crm_lead_intake head
       JOIN documents document ON document.id = head.document_id
      WHERE document.status = @posted_status
        AND document.posted_at_utc < (@as_of_utc::date + INTERVAL '1 day')) AS LeadCount,
    (SELECT COUNT(*)::integer
       FROM doc_crm_lead_qualification head
       JOIN documents document ON document.id = head.document_id
      WHERE document.status = @posted_status
        AND head.qualification_state <> 'Converted'
        AND document.posted_at_utc < (@as_of_utc::date + INTERVAL '1 day')) AS QualifiedLeadCount,
    ((SELECT COUNT(*)
        FROM doc_crm_lead_conversion head
        JOIN documents document ON document.id = head.document_id
       WHERE document.status = @posted_status
         AND document.posted_at_utc < (@as_of_utc::date + INTERVAL '1 day'))
     +
     (SELECT COUNT(*)
        FROM doc_crm_lead_qualification head
        JOIN documents document ON document.id = head.document_id
       WHERE document.status = @posted_status
         AND head.qualification_state = 'Converted'
         AND document.posted_at_utc < (@as_of_utc::date + INTERVAL '1 day')))::integer AS ConvertedLeadCount,
    COALESCE((SELECT SUM(amount) FROM crm_quotes_current WHERE quote_date <= @as_of_utc), 0) AS QuoteAmount,
    (SELECT COUNT(*)::integer FROM crm_quotes_current WHERE quote_date <= @as_of_utc) AS QuoteCount,
    (SELECT COUNT(*)::integer FROM crm_activities_current WHERE activity_date <= @as_of_utc) AS ActivityCount;

SELECT
    opportunity_id AS OpportunityId,
    COALESCE(opportunity_name, opportunity_id::text) AS Opportunity,
    COALESCE(account_display, 'Account') AS Account,
    COALESCE(stage_display, 'Stage') AS Stage,
    COALESCE(amount, 0) AS Amount,
    COALESCE(weighted_amount, 0) AS WeightedAmount
FROM crm_opportunities_current
WHERE COALESCE(amount, 0) <> 0 OR COALESCE(weighted_amount, 0) <> 0
ORDER BY COALESCE(weighted_amount, 0) DESC,
         COALESCE(amount, 0) DESC,
         COALESCE(opportunity_name, opportunity_id::text),
         opportunity_id
LIMIT @opportunity_limit;
""";

        await using var grid = await uow.Connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            new
            {
                posted_status = (short)DocumentStatus.Posted,
                as_of_utc = asOfUtc,
                opportunity_limit = opportunityLimit
            },
            transaction: uow.Transaction,
            cancellationToken: ct));

        var totals = await grid.ReadSingleAsync<DashboardTotalsRow>();
        var opportunities = (await grid.ReadAsync<CrmDashboardOpportunitySnapshot>()).AsList();

        return new CrmDashboardSnapshot(
            totals.PipelineAmount,
            totals.WeightedPipelineAmount,
            totals.LeadCount,
            totals.QualifiedLeadCount,
            totals.ConvertedLeadCount,
            totals.QuoteAmount,
            totals.QuoteCount,
            totals.ActivityCount,
            opportunities);
    }

    private sealed record DashboardTotalsRow(
        decimal PipelineAmount,
        decimal WeightedPipelineAmount,
        int LeadCount,
        int QualifiedLeadCount,
        int ConvertedLeadCount,
        decimal QuoteAmount,
        int QuoteCount,
        int ActivityCount);
}
