using NGB.Tools.Exceptions;

namespace NGB.Core.Security;

public sealed record NgbPermissionKey
{
    public NgbPermissionKey(string resourceKind, string resourceCode, string actionCode)
    {
        ResourceKind = NormalizeRequired(resourceKind, nameof(resourceKind), allowDots: false);
        ResourceCode = NormalizeRequired(resourceCode, nameof(resourceCode), allowDots: true);
        ActionCode = NormalizeRequired(actionCode, nameof(actionCode), allowDots: false);
    }

    public string ResourceKind { get; }

    public string ResourceCode { get; }

    public string ActionCode { get; }

    public static NgbPermissionKey Parse(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
            throw new NgbArgumentRequiredException(nameof(permission));

        var parts = permission.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            throw new NgbArgumentInvalidException(nameof(permission), "Permission key must have the form resource_kind.resource_code.action_code.");

        var resourceCode = string.Join('.', parts.Skip(1).Take(parts.Length - 2));
        return new NgbPermissionKey(parts[0], resourceCode, parts[^1]);
    }

    public override string ToString() => $"{ResourceKind}.{ResourceCode}.{ActionCode}";

    private static string NormalizeRequired(string value, string paramName, bool allowDots)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new NgbArgumentRequiredException(paramName);

        var normalized = value.Trim().ToLowerInvariant();
        if (!allowDots && normalized.Contains('.', StringComparison.Ordinal))
            throw new NgbArgumentInvalidException(paramName, "Permission key segments must not contain '.'.");

        return normalized;
    }
}
