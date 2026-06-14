namespace NGB.Contracts.Security;

public sealed record RoleBadgeDto(
    Guid RoleId,
    string Code,
    string Name,
    bool IsSystem,
    bool IsActive);

public sealed record RoleListItemDto(
    Guid RoleId,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    int AssignedUsersCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record RoleDetailsDto(
    Guid RoleId,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    IReadOnlyList<PermissionAssignmentDto> Permissions,
    IReadOnlyList<UserBadgeDto> AssignedUsers,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateRoleRequestDto(
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<PermissionAssignmentDto> Permissions);

public sealed record UpdateRoleRequestDto(
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<PermissionAssignmentDto> Permissions);

public sealed record ReplaceRolePermissionsRequestDto(IReadOnlyList<PermissionAssignmentDto> Permissions);
