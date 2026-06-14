using NGB.Contracts.Security;

namespace NGB.Runtime.Security;

public interface IEffectiveAccessService
{
    Task<EffectiveAccessDto> GetEffectiveAccessAsync(Guid userId, CancellationToken ct);
}
