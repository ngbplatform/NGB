using NGB.Core.Security;

namespace NGB.Persistence.Security;

public interface IPermissionSnapshotRepository
{
    Task<IReadOnlyList<NgbPermissionKey>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<NgbPermissionKey>> GetRolePermissionsAsync(Guid roleId, CancellationToken ct = default);

    Task ReplaceRolePermissionsAsync(
        Guid roleId,
        IReadOnlyList<NgbPermissionKey> permissions,
        CancellationToken ct = default);
}
