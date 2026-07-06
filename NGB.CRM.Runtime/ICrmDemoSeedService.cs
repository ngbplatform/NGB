using NGB.CRM.Contracts;

namespace NGB.CRM.Runtime;

public interface ICrmDemoSeedService
{
    Task<CrmDemoSeedResult> EnsureDemoAsync(CancellationToken ct = default);
}
