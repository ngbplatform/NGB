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

    public string AccessCacheKey { get; } = BuildAccessCacheKey(userId, isAuthenticated, isActive, isBootstrapAdmin, accessVersion);

    public IReadOnlySet<NgbPermissionKey> Permissions { get; } = permissions.ToHashSet();

    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlySet<string>>> PermissionIndex { get; }
        = BuildPermissionIndex(permissions);

    public bool Has(NgbPermissionKey permission)
        => Has(permission.ResourceKind, permission.ResourceCode, permission.ActionCode);

    public bool Has(string resourceKind, string resourceCode, string actionCode)
    {
        if (!TryNormalizeLookupPart(resourceKind, allowDots: false, out var normalizedResourceKind)
            || !TryNormalizeLookupPart(resourceCode, allowDots: true, out var normalizedResourceCode)
            || !TryNormalizeLookupPart(actionCode, allowDots: false, out var normalizedActionCode))
            return false;

        if (!IsAuthenticated || !IsActive)
            return false;

        if (IsBootstrapAdmin)
            return true;

        return PermissionIndex.TryGetValue(normalizedResourceKind, out var resources)
               && resources.TryGetValue(normalizedResourceCode, out var actions)
               && actions.Contains(normalizedActionCode);
    }

    public bool HasAny(string resourceKind, string actionCode)
    {
        if (!TryNormalizeLookupPart(resourceKind, allowDots: false, out var normalizedResourceKind)
            || !TryNormalizeLookupPart(actionCode, allowDots: false, out var normalizedActionCode))
        {
            return false;
        }

        if (!IsAuthenticated || !IsActive)
            return false;

        if (IsBootstrapAdmin)
            return true;

        return PermissionIndex.TryGetValue(normalizedResourceKind, out var resources)
               && resources.Values.Any(actions => actions.Contains(normalizedActionCode));
    }

    public static PermissionSnapshot Anonymous { get; } = new(
        userId: null,
        authSubject: null,
        isAuthenticated: false,
        isActive: false,
        isBootstrapAdmin: false,
        accessVersion: 0,
        permissions: []);

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlySet<string>>> BuildPermissionIndex(
        IReadOnlyCollection<NgbPermissionKey> permissions)
    {
        var byKind = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var permission in permissions)
        {
            if (!byKind.TryGetValue(permission.ResourceKind, out var byResource))
            {
                byResource = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                byKind.Add(permission.ResourceKind, byResource);
            }

            if (!byResource.TryGetValue(permission.ResourceCode, out var actions))
            {
                actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                byResource.Add(permission.ResourceCode, actions);
            }

            actions.Add(permission.ActionCode);
        }

        return byKind.ToDictionary(
            static x => x.Key,
            static x => (IReadOnlyDictionary<string, IReadOnlySet<string>>)x.Value.ToDictionary(
                static y => y.Key,
                static y => (IReadOnlySet<string>)y.Value,
                StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildAccessCacheKey(
        Guid? userId,
        bool isAuthenticated,
        bool isActive,
        bool isBootstrapAdmin,
        long accessVersion)
    {
        if (!isAuthenticated)
            return "anonymous";

        if (!isActive)
            return "inactive";

        if (isBootstrapAdmin)
            return "bootstrap-admin";

        return userId is null
            ? "authenticated-without-user"
            : $"user:{userId.Value:N}:v{Math.Max(accessVersion, 0)}";
    }

    private static bool TryNormalizeLookupPart(string? value, bool allowDots, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (!allowDots && trimmed.Contains('.', StringComparison.Ordinal))
            return false;

        normalized = trimmed;
        return true;
    }
}
