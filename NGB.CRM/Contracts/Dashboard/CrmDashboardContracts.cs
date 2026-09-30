namespace NGB.CRM.Contracts.Dashboard;

public interface ICrmDashboardService
{
    Task<CrmDashboardResponse> GetAsync(DateOnly asOfUtc, CancellationToken ct = default);
}

public sealed record CrmDashboardResponse(
    DateOnly AsOfUtc,
    decimal PipelineAmount,
    decimal WeightedPipelineAmount,
    int LeadCount,
    int QualifiedLeadCount,
    int ConvertedLeadCount,
    decimal QuoteAmount,
    int QuoteCount,
    int ActivityCount,
    IReadOnlyList<CrmDashboardOpportunity> OpenOpportunities);

public sealed record CrmDashboardOpportunity(
    Guid OpportunityId,
    string Opportunity,
    string Account,
    string Stage,
    decimal Amount,
    decimal WeightedAmount);
