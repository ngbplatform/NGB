namespace NGB.CRM.Reporting;

public interface ICrmDashboardReader
{
    Task<CrmDashboardSnapshot> GetAsync(DateOnly asOfUtc, int opportunityLimit, CancellationToken ct = default);
}

public sealed record CrmDashboardSnapshot(
    decimal PipelineAmount,
    decimal WeightedPipelineAmount,
    int LeadCount,
    int QualifiedLeadCount,
    int ConvertedLeadCount,
    decimal QuoteAmount,
    int QuoteCount,
    int ActivityCount,
    IReadOnlyList<CrmDashboardOpportunitySnapshot> OpenOpportunities);

public sealed record CrmDashboardOpportunitySnapshot(
    Guid OpportunityId,
    string Opportunity,
    string Account,
    string Stage,
    decimal Amount,
    decimal WeightedAmount);
