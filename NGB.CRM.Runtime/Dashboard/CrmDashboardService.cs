using NGB.CRM.Contracts.Dashboard;
using NGB.CRM.Reporting;

namespace NGB.CRM.Runtime.Dashboard;

public sealed class CrmDashboardService(ICrmDashboardReader reader) : ICrmDashboardService
{
    private const int OpportunityLimit = 6;

    public async Task<CrmDashboardResponse> GetAsync(DateOnly asOfUtc, CancellationToken ct = default)
    {
        var snapshot = await reader.GetAsync(asOfUtc, OpportunityLimit, ct);

        return new CrmDashboardResponse(
            asOfUtc,
            snapshot.PipelineAmount,
            snapshot.WeightedPipelineAmount,
            snapshot.LeadCount,
            snapshot.QualifiedLeadCount,
            snapshot.ConvertedLeadCount,
            snapshot.QuoteAmount,
            snapshot.QuoteCount,
            snapshot.ActivityCount,
            snapshot.OpenOpportunities.Select(static item => new CrmDashboardOpportunity(
                item.OpportunityId,
                item.Opportunity,
                item.Account,
                item.Stage,
                item.Amount,
                item.WeightedAmount)).ToArray());
    }
}
