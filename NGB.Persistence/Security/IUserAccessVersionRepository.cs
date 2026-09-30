using NGB.Core.Security;

namespace NGB.Persistence.Security;

public interface IUserAccessVersionRepository
{
    Task<PlatformUserAccessVersion?> GetAsync(Guid userId, CancellationToken ct = default);

    Task<PlatformUserAccessVersion> GetOrCreateAsync(Guid userId, CancellationToken ct = default);

    Task<PlatformUserAccessVersion> IncrementAsync(Guid userId, CancellationToken ct = default);

    Task IncrementManyAsync(IReadOnlyList<Guid> userIds, CancellationToken ct = default);

    Task IncrementForRoleAsync(Guid roleId, CancellationToken ct = default);
}
