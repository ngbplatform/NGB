using NGB.Core.Security;

namespace NGB.Core.Documents.Actions;

public sealed class DocumentActionSecurityContext(
    Guid? userId,
    bool isAuthenticated,
    bool isActive,
    bool isBootstrapAdmin,
    IReadOnlySet<NgbPermissionKey> permissions)
{
    public Guid? UserId { get; } = userId;
    public bool IsAuthenticated { get; } = isAuthenticated;
    public bool IsActive { get; } = isActive;
    public bool IsBootstrapAdmin { get; } = isBootstrapAdmin;
    public IReadOnlySet<NgbPermissionKey> Permissions { get; } = permissions;

    public bool Has(string resourceKind, string resourceCode, string actionCode)
        => IsAuthenticated
           && IsActive
           && (IsBootstrapAdmin || Permissions.Contains(new NgbPermissionKey(resourceKind, resourceCode, actionCode)));
}
