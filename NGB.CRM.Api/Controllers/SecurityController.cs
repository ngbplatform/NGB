using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Runtime.Security;

namespace NGB.CRM.Api.Controllers;

[Authorize]
[ApiController]
public sealed class SecurityController(
    ICurrentAccessService currentAccess,
    PermissionDefinitionRegistry permissionDefinitions,
    IUserAccessManagementService users,
    IRoleManagementService roles,
    IEffectiveAccessService effectiveAccess,
    INgbAccessChecker access)
    : SecurityControllerBase(currentAccess, permissionDefinitions, users, roles, effectiveAccess, access);
