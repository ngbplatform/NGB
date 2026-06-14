using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Core.Security;
using NGB.PropertyManagement.Contracts;
using NGB.PropertyManagement.PostgreSql.Bootstrap;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.Admin;
using NGB.Runtime.Security;

namespace NGB.PropertyManagement.Api.Controllers;

[Authorize]
[ApiController]
public sealed class AdminController(PermissionAwareAdminService service, INgbAccessChecker access)
    : AdminControllerBase(service)
{
    /// <summary>
    /// Idempotent initializer for PM defaults (accounts, operational registers, accounting policy).
    /// Designed to be invoked by the Setup UI.
    /// </summary>
    [HttpPost("~/api/admin/setup/apply-defaults")]
    public async Task<PropertyManagementSetupResult> ApplyDefaults(
        [FromServices] IPropertyManagementSetupService setupService,
        [FromServices] PropertyManagementSecuritySeeder securitySeeder,
        CancellationToken ct)
    {
        await access.RequireAsync(NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.Manage, ct);
        var result = await setupService.EnsureDefaultsAsync(ct);
        await securitySeeder.EnsureSeededAsync(ct);
        return result;
    }
}
