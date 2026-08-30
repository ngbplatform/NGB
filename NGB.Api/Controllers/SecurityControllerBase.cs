using Microsoft.AspNetCore.Mvc;
using NGB.Contracts.Common;
using NGB.Contracts.Security;
using NGB.Core.Security;
using NGB.Runtime.Security;

namespace NGB.Api.Controllers;

public abstract class SecurityControllerBase(
    ICurrentAccessService currentAccess,
    PermissionDefinitionRegistry permissionDefinitions,
    IUserAccessManagementService users,
    IRoleManagementService roles,
    IEffectiveAccessService effectiveAccess,
    INgbAccessChecker access)
    : ControllerBase
{
    [HttpGet("~/api/security/me/access")]
    public Task<CurrentAccessDto> GetCurrentAccess(CancellationToken ct) => currentAccess.GetCurrentAccessAsync(ct);

    [HttpGet("~/api/security/permissions/definitions")]
    public async Task<IReadOnlyList<PermissionDefinitionDto>> GetPermissionDefinitions(CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.PermissionsView, ct);
        return await permissionDefinitions.GetAllAsync(ct);
    }

    [HttpGet("~/api/security/users")]
    public async Task<PageResponseDto<UserListItemDto>> GetUsers(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = PagingLimits.DefaultPageSize,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        await RequireAsync(NgbSystemPermissions.UsersView, ct);
        return await users.GetUsersAsync(new UserPageRequestDto(offset, limit, isActive, cursor), ct);
    }

    [HttpPost("~/api/security/users")]
    public async Task<UserDetailsDto> CreateUser([FromBody] CreateUserRequestDto request, CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.UsersManage, ct);
        return await users.CreateUserAsync(request, ct);
    }

    [HttpGet("~/api/security/users/{userId:guid}")]
    public async Task<UserDetailsDto> GetUser([FromRoute] Guid userId, CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.UsersView, ct);
        return await users.GetUserAsync(userId, ct);
    }

    [HttpPut("~/api/security/users/{userId:guid}")]
    public async Task<UserDetailsDto> UpdateUser(
        [FromRoute] Guid userId,
        [FromBody] UpdateUserRequestDto request,
        CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.UsersManage, ct);
        return await users.UpdateUserAsync(userId, request, ct);
    }

    [HttpPost("~/api/security/users/{userId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateUser([FromRoute] Guid userId, CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.UsersManage, ct);
        await users.DeactivateUserAsync(userId, ct);
        return NoContent();
    }

    [HttpPost("~/api/security/users/{userId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateUser([FromRoute] Guid userId, CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.UsersManage, ct);
        await users.ReactivateUserAsync(userId, ct);
        return NoContent();
    }

    [HttpPut("~/api/security/users/{userId:guid}/roles")]
    public async Task<IActionResult> ReplaceUserRoles(
        [FromRoute] Guid userId,
        [FromBody] ReplaceUserRolesRequestDto request,
        CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.UsersManage, ct);
        await users.ReplaceUserRolesAsync(userId, request, ct);
        return NoContent();
    }

    [HttpGet("~/api/security/users/{userId:guid}/effective-access")]
    public async Task<EffectiveAccessDto> GetUserEffectiveAccess([FromRoute] Guid userId, CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.UsersView, ct);
        return await effectiveAccess.GetEffectiveAccessAsync(userId, ct);
    }

    [HttpGet("~/api/security/roles")]
    public async Task<IReadOnlyList<RoleListItemDto>> GetRoles(CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.RolesView, ct);
        return await roles.GetRolesAsync(ct);
    }

    [HttpPost("~/api/security/roles")]
    public async Task<RoleDetailsDto> CreateRole([FromBody] CreateRoleRequestDto request, CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.RolesManage, ct);
        return await roles.CreateRoleAsync(request, ct);
    }

    [HttpGet("~/api/security/roles/{roleId:guid}")]
    public async Task<RoleDetailsDto> GetRole([FromRoute] Guid roleId, CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.RolesView, ct);
        return await roles.GetRoleAsync(roleId, ct);
    }

    [HttpPut("~/api/security/roles/{roleId:guid}")]
    public async Task<RoleDetailsDto> UpdateRole(
        [FromRoute] Guid roleId,
        [FromBody] UpdateRoleRequestDto request,
        CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.RolesManage, ct);
        return await roles.UpdateRoleAsync(roleId, request, ct);
    }

    [HttpPost("~/api/security/roles/{roleId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateRole([FromRoute] Guid roleId, CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.RolesManage, ct);
        await roles.DeactivateRoleAsync(roleId, ct);
        return NoContent();
    }

    [HttpPost("~/api/security/roles/{roleId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateRole([FromRoute] Guid roleId, CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.RolesManage, ct);
        await roles.ReactivateRoleAsync(roleId, ct);
        return NoContent();
    }

    [HttpPut("~/api/security/roles/{roleId:guid}/permissions")]
    public async Task<IActionResult> ReplaceRolePermissions(
        [FromRoute] Guid roleId,
        [FromBody] ReplaceRolePermissionsRequestDto request,
        CancellationToken ct)
    {
        await RequireAsync(NgbSystemPermissions.RolesManage, ct);
        await roles.ReplaceRolePermissionsAsync(roleId, request, ct);
        return NoContent();
    }

    private Task RequireAsync(NgbPermissionKey permission, CancellationToken ct)
        => access.RequireAsync(permission.ResourceKind, permission.ResourceCode, permission.ActionCode, ct);
}
