using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Core.Security;
using NGB.CRM.Contracts;
using NGB.CRM.Runtime;
using NGB.Runtime.Admin;
using NGB.Runtime.Security;

namespace NGB.CRM.Api.Controllers;

[Authorize]
[ApiController]
public sealed class AdminController(PermissionAwareAdminService service) : AdminControllerBase(service)
{
    [HttpPost("~/api/admin/setup/apply-defaults")]
    public async Task<CrmSetupResult> ApplyDefaults(
        [FromServices] ICrmSetupService setupService,
        [FromServices] INgbAccessChecker access,
        CancellationToken ct)
    {
        await access.RequireAsync(NgbResourceKinds.System, NgbPermissionResources.Roles, NgbPermissionActions.Manage, ct);
        await access.RequireAsync(NgbResourceKinds.System, NgbPermissionResources.Users, NgbPermissionActions.Manage, ct);
        return await setupService.EnsureDefaultsAsync(ct);
    }
}
