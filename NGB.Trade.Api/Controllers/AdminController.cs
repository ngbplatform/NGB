using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Runtime.Admin;
using NGB.Trade.Contracts;
using NGB.Trade.Runtime;

namespace NGB.Trade.Api.Controllers;

[Authorize]
[ApiController]
public sealed class AdminController(PermissionAwareAdminService service) : AdminControllerBase(service)
{
    [HttpPost("~/api/admin/setup/apply-defaults")]
    public Task<TradeSetupResult> ApplyDefaults(
        [FromServices] ITradeSetupService setupService,
        CancellationToken ct)
        => setupService.EnsureDefaultsAsync(ct);
}
