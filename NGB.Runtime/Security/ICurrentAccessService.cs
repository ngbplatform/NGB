using NGB.Contracts.Security;

namespace NGB.Runtime.Security;

public interface ICurrentAccessService
{
    Task<CurrentAccessDto> GetCurrentAccessAsync(CancellationToken ct);
}
