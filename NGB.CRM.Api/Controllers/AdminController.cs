using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.CRM.Contracts;
using NGB.CRM.Runtime;
using NGB.Runtime.Admin;

namespace NGB.CRM.Api.Controllers;

[Authorize]
[ApiController]
public sealed class AdminController(PermissionAwareAdminService service) : AdminControllerBase(service)
{
    [HttpPost("~/api/admin/setup/apply-defaults")]
    public Task<CrmSetupResult> ApplyDefaults(
        [FromServices] ICrmSetupService setupService,
        CancellationToken ct)
        => setupService.EnsureDefaultsAsync(ct);
}
