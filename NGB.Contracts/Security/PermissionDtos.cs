namespace NGB.Contracts.Security;

public sealed record PermissionKeyDto(string ResourceKind, string ResourceCode, string ActionCode);

public sealed record PermissionDefinitionDto(
    string ResourceKind,
    string ResourceCode,
    string ActionCode,
    string DisplayName,
    string Group,
    string? Description = null);

public sealed record PermissionAssignmentDto(string ResourceKind, string ResourceCode, string ActionCode);

public sealed record PermissionGroupDto(string Group, IReadOnlyList<PermissionDefinitionDto> Permissions);
