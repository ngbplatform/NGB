namespace NGB.AgencyBilling.Runtime.Policy;

public interface IAgencyBillingAccountingPolicyReader
{
    Task<AgencyBillingAccountingPolicy> GetRequiredAsync(CancellationToken ct = default);
}
