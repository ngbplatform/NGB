using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.Services;
using NGB.PropertyManagement.Contracts;
using NGB.PropertyManagement.Runtime;

namespace NGB.PropertyManagement.Api.Controllers;

[Authorize]
[ApiController]
public sealed class AdminController(IAdminService service) : AdminControllerBase(service)
{
    /// <summary>
    /// Idempotent initializer for PM defaults (accounts, operational registers, accounting policy).
    /// Designed to be invoked by the Setup UI.
    /// </summary>
    [HttpPost("~/api/admin/setup/apply-defaults")]
    public Task<PropertyManagementSetupResult> ApplyDefaults(
        [FromServices] IPropertyManagementSetupService setupService,
        CancellationToken ct)
        => setupService.EnsureDefaultsAsync(ct);
}
