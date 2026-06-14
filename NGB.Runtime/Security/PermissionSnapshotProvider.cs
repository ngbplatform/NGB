using Microsoft.Extensions.Caching.Memory;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;

namespace NGB.Runtime.Security;

public sealed class PermissionSnapshotProvider(
    ICurrentActorContext currentActor,
    IPlatformUserRepository users,
    IUserAccessVersionRepository versions,
    IPermissionSnapshotRepository permissions,
    IMemoryCache cache)
    : IPermissionSnapshotProvider
{
    private const string BootstrapAdminRole = "ngb-admin";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(45);

    public async Task<PermissionSnapshot> GetCurrentAsync(CancellationToken ct)
    {
        var actor = currentActor.Current;
        if (actor is null)
            return PermissionSnapshot.Anonymous;

        var isBootstrapAdmin = actor.HasAuthRole(BootstrapAdminRole);
        if (!actor.IsActive)
        {
            return new PermissionSnapshot(
                userId: null,
                authSubject: actor.AuthSubject,
                isAuthenticated: true,
                isActive: false,
                isBootstrapAdmin: false,
                accessVersion: 0,
                permissions: []);
        }

        var platformUser = await users.GetByAuthSubjectAsync(actor.AuthSubject, ct);

        if (platformUser is null)
        {
            return new PermissionSnapshot(
                userId: null,
                authSubject: actor.AuthSubject,
                isAuthenticated: true,
                isActive: isBootstrapAdmin,
                isBootstrapAdmin: isBootstrapAdmin,
                accessVersion: 0,
                permissions: []);
        }

        if (!platformUser.IsActive)
        {
            return new PermissionSnapshot(
                userId: platformUser.UserId,
                authSubject: actor.AuthSubject,
                isAuthenticated: true,
                isActive: false,
                isBootstrapAdmin: false,
                accessVersion: 0,
                permissions: []);
        }

        var version = await versions.GetAsync(platformUser.UserId, ct);
        var accessVersion = version?.Version ?? 1;
        var cacheKey = $"ngb:security:snapshot:{platformUser.UserId:N}:{accessVersion}";

        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;

            var effectivePermissions = isBootstrapAdmin
                ? []
                : await permissions.GetEffectivePermissionsAsync(platformUser.UserId, ct);

            return new PermissionSnapshot(
                userId: platformUser.UserId,
                authSubject: actor.AuthSubject,
                isAuthenticated: true,
                isActive: true,
                isBootstrapAdmin: isBootstrapAdmin,
                accessVersion: accessVersion,
                permissions: effectivePermissions);
        }) ?? PermissionSnapshot.Anonymous;
    }
}
