namespace NGB.Contracts.Security;

public sealed record CurrentAccessDto(
    Guid? UserId,
    string? AuthSubject,
    bool IsAuthenticated,
    bool IsActive,
    bool IsBootstrapAdmin,
    long AccessVersion,
    IReadOnlyList<RoleBadgeDto> Roles,
    IReadOnlyList<PermissionAssignmentDto> Permissions);

public sealed record EffectiveAccessDto(
    Guid UserId,
    long AccessVersion,
    IReadOnlyList<EffectiveAccessGroupDto> Groups);

public sealed record EffectiveAccessGroupDto(string Group, IReadOnlyList<EffectiveAccessResourceDto> Resources);

public sealed record EffectiveAccessResourceDto(
    string ResourceKind,
    string ResourceCode,
    string DisplayName,
    IReadOnlyList<string> Actions);
