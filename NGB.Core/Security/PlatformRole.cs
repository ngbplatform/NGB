namespace NGB.Core.Security;

public sealed record PlatformRole(
    Guid RoleId,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
