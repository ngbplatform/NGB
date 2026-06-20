using NGB.Core.Security;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Security;

public sealed class NgbPermissionDeniedException(NgbPermissionKey permission)
    : NgbForbiddenException(
        message: "Permission denied.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["resourceKind"] = permission.ResourceKind,
            ["resourceCode"] = permission.ResourceCode,
            ["actionCode"] = permission.ActionCode
        })
{
    public const string Code = "permission_denied";

    public NgbPermissionKey Permission { get; } = permission;
}
