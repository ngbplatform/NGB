using NGB.Core.Security;

namespace NGB.Persistence.Security;

public interface IPlatformUserRoleRepository
{
    Task<IReadOnlyList<PlatformRole>> GetRolesForUserAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<PlatformRole>>> GetRolesForUsersAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetUserIdsForRoleAsync(Guid roleId, CancellationToken ct = default);

    Task ReplaceUserRolesAsync(
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        Guid? assignedByUserId,
        CancellationToken ct = default);
}
