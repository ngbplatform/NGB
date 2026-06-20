using NGB.Tools.Exceptions;

namespace NGB.Runtime.Security;

public sealed class SecurityUserNotFoundException(Guid userId)
    : NgbNotFoundException(
        message: $"Security user '{userId}' was not found.",
        errorCode: Code,
        context: new Dictionary<string, object?> { ["userId"] = userId })
{
    public const string Code = "ngb.security.user_not_found";
}

public sealed class SecurityRoleNotFoundException(Guid roleId)
    : NgbNotFoundException(
        message: $"Security role '{roleId}' was not found.",
        errorCode: Code,
        context: new Dictionary<string, object?> { ["roleId"] = roleId })
{
    public const string Code = "ngb.security.role_not_found";
}
