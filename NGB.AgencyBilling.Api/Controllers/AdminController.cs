using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.AgencyBilling.Contracts;
using NGB.AgencyBilling.Runtime;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.Services;

namespace NGB.AgencyBilling.Api.Controllers;

[Authorize]
[ApiController]
public sealed class AdminController(IAdminService service) : AdminControllerBase(service)
{
    [HttpPost("~/api/admin/setup/apply-defaults")]
    public Task<AgencyBillingSetupResult> ApplyDefaults(
        [FromServices] IAgencyBillingSetupService setupService,
        CancellationToken ct)
        => setupService.EnsureDefaultsAsync(ct);
}
