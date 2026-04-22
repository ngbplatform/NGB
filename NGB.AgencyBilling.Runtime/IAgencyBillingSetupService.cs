using NGB.AgencyBilling.Contracts;

namespace NGB.AgencyBilling.Runtime;

public interface IAgencyBillingSetupService
{
    Task<AgencyBillingSetupResult> EnsureDefaultsAsync(CancellationToken ct = default);
}
