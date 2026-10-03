using Microsoft.Extensions.Options;
using NGB.Core.Security;
using NGB.Persistence.Security;

namespace NGB.Runtime.Security;

public sealed class EffectivePermissionService(
    IPermissionSnapshotRepository permissions,
    IPlatformUserRoleRepository userRoles,
    PermissionDefinitionRegistry definitions,
    IOptions<NgbAdministratorOptions> options)
{
    public async Task<IReadOnlyList<NgbPermissionKey>> GetAsync(Guid userId, CancellationToken ct)
    {
        if (options.Value.ApplicationRoleCodes.Count > 0)
        {
            var roles = await userRoles.GetRolesForUserAsync(userId, ct);
            if (roles.Any(role => role.IsActive && options.Value.IsAdministratorRoleCode(role.Code)))
            {
                return (await definitions.GetAllAsync(ct))
                    .Select(static permission => new NgbPermissionKey(
                        permission.ResourceKind,
                        permission.ResourceCode,
                        permission.ActionCode))
                    .ToArray();
            }
        }

        return await permissions.GetEffectivePermissionsAsync(userId, ct);
    }
}
