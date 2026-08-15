using NGB.Persistence.Security;

namespace NGB.Runtime.Security;

public sealed class PermissionSnapshotProvider(
    ICurrentActorContext currentActor,
    IPermissionSnapshotRepository permissions,
    NgbSecurityCache cache)
    : IPermissionSnapshotProvider
{
    private const string BootstrapAdminRole = "ngb-admin";
    private Task<PermissionSnapshot>? _currentSnapshotTask;

    public async Task<PermissionSnapshot> GetCurrentAsync(CancellationToken ct)
    {
        var task = _currentSnapshotTask ??= LoadCurrentAsync(ct);

        try
        {
            return await task;
        }
        catch
        {
            if (ReferenceEquals(_currentSnapshotTask, task))
                _currentSnapshotTask = null;

            throw;
        }
    }

    public Task<PermissionSnapshot> RefreshCurrentAsync(CancellationToken ct)
    {
        _currentSnapshotTask = null;
        return GetCurrentAsync(ct);
    }

    private async Task<PermissionSnapshot> LoadCurrentAsync(CancellationToken ct)
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

        var platformUser = await permissions.GetUserAccessStateByAuthSubjectAsync(actor.AuthSubject, ct);

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

        var accessVersion = platformUser.AccessVersion <= 0 ? 1 : platformUser.AccessVersion;

        return await cache.GetOrCreatePermissionSnapshotAsync(
            platformUser.UserId,
            accessVersion,
            async token =>
            {
                var effectivePermissions = isBootstrapAdmin
                    ? []
                    : await permissions.GetEffectivePermissionsAsync(platformUser.UserId, token);

                return new PermissionSnapshot(
                    userId: platformUser.UserId,
                    authSubject: actor.AuthSubject,
                    isAuthenticated: true,
                    isActive: true,
                    isBootstrapAdmin: isBootstrapAdmin,
                    accessVersion: accessVersion,
                    permissions: effectivePermissions);
            },
            ct) ?? PermissionSnapshot.Anonymous;
    }
}
