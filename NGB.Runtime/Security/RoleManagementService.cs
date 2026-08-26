using NGB.Contracts.Security;
using NGB.Core.AuditLog;
using NGB.Core.Security;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Security;

public sealed class RoleManagementService(
    IUnitOfWork uow,
    IPlatformRoleRepository roles,
    IPlatformUserRoleRepository userRoles,
    IPermissionSnapshotRepository permissions,
    IUserAccessVersionRepository versions,
    IPlatformUserRepository users,
    IAuditLogService audit)
    : IRoleManagementService
{
    private const int MaxRoleListSize = 500;

    public async Task<IReadOnlyList<RoleListItemDto>> GetRolesAsync(CancellationToken ct)
    {
        var all = await roles.GetListAsync(MaxRoleListSize, ct);

        return all
            .Select(item => new RoleListItemDto(
                item.Role.RoleId,
                item.Role.Code,
                item.Role.Name,
                item.Role.Description,
                item.Role.IsSystem,
                item.Role.IsActive,
                item.AssignedUserCount,
                item.Role.CreatedAtUtc,
                item.Role.UpdatedAtUtc))
            .ToArray();
    }

    public async Task<RoleDetailsDto> GetRoleAsync(Guid roleId, CancellationToken ct)
    {
        var role = await roles.GetByIdAsync(roleId, ct) ?? throw new SecurityRoleNotFoundException(roleId);
        var perms = await permissions.GetRolePermissionsAsync(roleId, ct);
        var userIds = await userRoles.GetUserIdsForRoleAsync(roleId, ct);
        var usersById = await users.GetByIdsAsync(userIds, ct);

        return new RoleDetailsDto(
            role.RoleId,
            role.Code,
            role.Name,
            role.Description,
            role.IsSystem,
            role.IsActive,
            perms.Select(ToDto).ToArray(),
            usersById.Values
                .OrderBy(x => x.DisplayName ?? x.Email ?? x.AuthSubject, StringComparer.OrdinalIgnoreCase)
                .Select(static x => new UserBadgeDto(x.UserId, x.Email, x.DisplayName, x.IsActive))
                .ToArray(),
            role.CreatedAtUtc,
            role.UpdatedAtUtc);
    }

    public async Task<RoleDetailsDto> CreateRoleAsync(CreateRoleRequestDto request, CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var roleId = Guid.CreateVersion7();
        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var normalizedPermissions = Normalize(request.Permissions);
            await roles.UpsertAsync(roleId, request.Code, request.Name, request.Description, isSystem: false, isActive: true, innerCt);
            await permissions.ReplaceRolePermissionsAsync(roleId, normalizedPermissions, innerCt);
            await audit.WriteAsync(
                AuditEntityKind.SecurityRole,
                roleId,
                AuditActionCodes.SecurityRoleCreate,
                changes: BuildRoleAuditChanges(
                    oldRole: null,
                    newCode: request.Code,
                    newName: request.Name,
                    newDescription: request.Description,
                    newIsSystem: false,
                    newIsActive: true,
                    oldPermissions: Array.Empty<NgbPermissionKey>(),
                    newPermissions: normalizedPermissions),
                metadata: new { request.Code, request.Name, permissions = request.Permissions },
                ct: innerCt);
        }, ct);

        return await GetRoleAsync(roleId, ct);
    }

    public async Task<RoleDetailsDto> UpdateRoleAsync(Guid roleId, UpdateRoleRequestDto request, CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var existing = await roles.GetByIdAsync(roleId, ct) ?? throw new SecurityRoleNotFoundException(roleId);
        var oldPermissions = await permissions.GetRolePermissionsAsync(roleId, ct);

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var normalizedPermissions = Normalize(request.Permissions);
            await roles.UpsertAsync(roleId, request.Code, request.Name, request.Description, existing.IsSystem, request.IsActive, innerCt);
            await permissions.ReplaceRolePermissionsAsync(roleId, normalizedPermissions, innerCt);
            await IncrementRoleUsersAsync(roleId, innerCt);
            await audit.WriteAsync(
                AuditEntityKind.SecurityRole,
                roleId,
                AuditActionCodes.SecurityRoleUpdate,
                changes: BuildRoleAuditChanges(
                    existing,
                    request.Code,
                    request.Name,
                    request.Description,
                    existing.IsSystem,
                    request.IsActive,
                    oldPermissions,
                    normalizedPermissions),
                metadata: new { request.Code, request.Name, request.IsActive, permissions = request.Permissions },
                ct: innerCt);
        }, ct);

        return await GetRoleAsync(roleId, ct);
    }

    public Task DeactivateRoleAsync(Guid roleId, CancellationToken ct)
        => SetRoleActiveAsync(roleId, isActive: false, AuditActionCodes.SecurityRoleDeactivate, ct);

    public Task ReactivateRoleAsync(Guid roleId, CancellationToken ct)
        => SetRoleActiveAsync(roleId, isActive: true, AuditActionCodes.SecurityRoleReactivate, ct);

    public async Task ReplaceRolePermissionsAsync(Guid roleId, ReplaceRolePermissionsRequestDto request, CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        _ = await roles.GetByIdAsync(roleId, ct) ?? throw new SecurityRoleNotFoundException(roleId);
        var oldPermissions = await permissions.GetRolePermissionsAsync(roleId, ct);

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var normalizedPermissions = Normalize(request.Permissions);
            await permissions.ReplaceRolePermissionsAsync(roleId, normalizedPermissions, innerCt);
            await IncrementRoleUsersAsync(roleId, innerCt);
            await audit.WriteAsync(
                AuditEntityKind.SecurityRole,
                roleId,
                AuditActionCodes.SecurityRolePermissionsReplace,
                changes:
                [
                    AuditLogService.Change(NgbPermissionResources.Permissions, ToAuditPermissions(oldPermissions), ToAuditPermissions(normalizedPermissions))
                ],
                metadata: new { permissions = request.Permissions },
                ct: innerCt);
        }, ct);
    }

    private async Task SetRoleActiveAsync(Guid roleId, bool isActive, string auditAction, CancellationToken ct)
    {
        var role = await roles.GetByIdAsync(roleId, ct) ?? throw new SecurityRoleNotFoundException(roleId);

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            await roles.SetActiveAsync(roleId, isActive, innerCt);
            await IncrementRoleUsersAsync(roleId, innerCt);
            await audit.WriteAsync(
                AuditEntityKind.SecurityRole,
                roleId,
                auditAction,
                changes:
                [
                    AuditLogService.Change("status", ToAuditStatus(role.IsActive), ToAuditStatus(isActive))
                ],
                metadata: new { isActive },
                ct: innerCt);
        }, ct);
    }

    private async Task IncrementRoleUsersAsync(Guid roleId, CancellationToken ct)
    {
        var affectedUsers = await userRoles.GetUserIdsForRoleAsync(roleId, ct);
        await versions.IncrementManyAsync(affectedUsers, ct);
    }

    private static IReadOnlyList<NgbPermissionKey> Normalize(IReadOnlyList<PermissionAssignmentDto> assignments)
    {
        if (assignments is null)
            throw new NgbArgumentRequiredException(nameof(assignments));

        return assignments
            .Select(static x => new NgbPermissionKey(x.ResourceKind, x.ResourceCode, x.ActionCode))
            .Distinct()
            .ToArray();
    }

    private static PermissionAssignmentDto ToDto(NgbPermissionKey key)
        => new(key.ResourceKind, key.ResourceCode, key.ActionCode);

    private static IReadOnlyList<AuditFieldChange> BuildRoleAuditChanges(
        PlatformRole? oldRole,
        string newCode,
        string newName,
        string? newDescription,
        bool newIsSystem,
        bool newIsActive,
        IReadOnlyList<NgbPermissionKey> oldPermissions,
        IReadOnlyList<NgbPermissionKey> newPermissions)
        =>
        [
            AuditLogService.Change("code", oldRole?.Code, newCode),
            AuditLogService.Change("name", oldRole?.Name, newName),
            AuditLogService.Change("description", oldRole?.Description, newDescription),
            AuditLogService.Change("status", oldRole is null ? null : ToAuditStatus(oldRole.IsActive), ToAuditStatus(newIsActive)),
            AuditLogService.Change("system", oldRole is null ? null : ToAuditYesNo(oldRole.IsSystem), ToAuditYesNo(newIsSystem)),
            AuditLogService.Change("permissions", ToAuditPermissions(oldPermissions), ToAuditPermissions(newPermissions))
        ];

    private static object[] ToAuditPermissions(IReadOnlyList<NgbPermissionKey> permissions)
        => permissions
            .OrderBy(static permission => permission.ResourceKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static permission => permission.ResourceCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static permission => permission.ActionCode, StringComparer.OrdinalIgnoreCase)
            .Select(static permission => new
            {
                display = $"{HumanizeCode(permission.ResourceCode)}: {HumanizeCode(permission.ActionCode)}",
                key = permission.ToString(),
                resourceKind = permission.ResourceKind,
                resourceCode = permission.ResourceCode,
                action = permission.ActionCode
            })
            .Cast<object>()
            .ToArray();

    private static string HumanizeCode(string value)
    {
        var words = value
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(' ', words.Select(static word => word.Length switch
        {
            1 => word.ToUpperInvariant(),
            _ => char.ToUpperInvariant(word[0]) + word[1..]
        }));
    }

    private static string ToAuditStatus(bool isActive) => isActive ? "Active" : "Inactive";

    private static string ToAuditYesNo(bool value) => value ? "Yes" : "No";
}
