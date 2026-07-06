using NGB.CRM.Contracts;

namespace NGB.CRM.Runtime;

public interface ICrmSetupService
{
    Task<CrmSetupResult> EnsureDefaultsAsync(CancellationToken ct = default);
}
