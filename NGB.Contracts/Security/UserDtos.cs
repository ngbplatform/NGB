namespace NGB.Contracts.Security;

public sealed record UserBadgeDto(Guid UserId, string? Email, string? DisplayName, bool IsActive);

public sealed record UserListItemDto(
    Guid UserId,
    string AuthSubject,
    string? Email,
    string? DisplayName,
    bool IsActive,
    bool? KeycloakEnabled,
    IReadOnlyList<RoleBadgeDto> Roles,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record UserDetailsDto(
    Guid UserId,
    string AuthSubject,
    string? Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    bool IsActive,
    bool? KeycloakEnabled,
    IReadOnlyList<RoleBadgeDto> Roles,
    long AccessVersion,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateUserRequestDto(
    string Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    bool Enabled,
    string? TemporaryPassword,
    bool RequirePasswordUpdate,
    IReadOnlyList<Guid> RoleIds);

public sealed record UpdateUserRequestDto(
    string? Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    bool Enabled,
    string? TemporaryPassword,
    bool RequirePasswordUpdate,
    IReadOnlyList<Guid> RoleIds);

public sealed record ReplaceUserRolesRequestDto(IReadOnlyList<Guid> RoleIds);
