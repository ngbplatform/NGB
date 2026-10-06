using NGB.Tools.Exceptions;

namespace NGB.Runtime.Security;

public sealed class SecurityAdministratorRoleProtectedException(string roleCode, string operation)
    : NgbConflictException(
        message: $"Administrator role '{roleCode}' is protected. Changing its {operation} is not allowed.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["roleCode"] = roleCode,
            ["operation"] = operation
        })
{
    public const string Code = "ngb.security.administrator_role_protected";
}
