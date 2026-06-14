using NGB.Core.Security;

namespace NGB.Runtime.Security;

public sealed class PermissionSnapshot(
    Guid? userId,
    string? authSubject,
    bool isAuthenticated,
    bool isActive,
    bool isBootstrapAdmin,
    long accessVersion,
    IReadOnlyCollection<NgbPermissionKey> permissions)
{
    public Guid? UserId { get; } = userId;

    public string? AuthSubject { get; } = authSubject;

    public bool IsAuthenticated { get; } = isAuthenticated;

    public bool IsActive { get; } = isActive;

    public bool IsBootstrapAdmin { get; } = isBootstrapAdmin;

    public long AccessVersion { get; } = accessVersion;

    public IReadOnlySet<NgbPermissionKey> Permissions { get; } = permissions.ToHashSet();

    public bool Has(NgbPermissionKey permission)
    {
        if (!IsAuthenticated || !IsActive)
            return false;

        return IsBootstrapAdmin || Permissions.Contains(permission);
    }

    public static PermissionSnapshot Anonymous { get; } = new(
        userId: null,
        authSubject: null,
        isAuthenticated: false,
        isActive: false,
        isBootstrapAdmin: false,
        accessVersion: 0,
        permissions: []);
}
