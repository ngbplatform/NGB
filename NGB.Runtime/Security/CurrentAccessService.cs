using NGB.Contracts.Security;
using NGB.Persistence.Security;

namespace NGB.Runtime.Security;

public sealed class CurrentAccessService(IPermissionSnapshotProvider snapshots, IPlatformUserRoleRepository userRoles)
    : ICurrentAccessService
{
    public async Task<CurrentAccessDto> GetCurrentAccessAsync(CancellationToken ct)
    {
        var snapshot = await snapshots.GetCurrentAsync(ct);
        var roles = snapshot is { UserId: { } userId, IsAuthenticated: true, IsActive: true }
            ? await userRoles.GetRolesForUserAsync(userId, ct)
            : [];

        return new CurrentAccessDto(
            UserId: snapshot.UserId,
            AuthSubject: snapshot.AuthSubject,
            IsAuthenticated: snapshot.IsAuthenticated,
            IsActive: snapshot.IsActive,
            IsBootstrapAdmin: snapshot.IsBootstrapAdmin,
            AccessVersion: snapshot.AccessVersion,
            Roles: roles
                .Where(static x => x.IsActive)
                .Select(static x => new RoleBadgeDto(x.RoleId, x.Code, x.Name, x.IsSystem, x.IsActive))
                .OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static x => x.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Permissions: snapshot.Permissions
                .Select(static x => new PermissionAssignmentDto(x.ResourceKind, x.ResourceCode, x.ActionCode))
                .OrderBy(static x => x.ResourceKind, StringComparer.Ordinal)
                .ThenBy(static x => x.ResourceCode, StringComparer.Ordinal)
                .ThenBy(static x => x.ActionCode, StringComparer.Ordinal)
                .ToArray());
    }
}
