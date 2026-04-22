using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.Services;
using NGB.Trade.Contracts;
using NGB.Trade.Runtime;

namespace NGB.Trade.Api.Controllers;

[Authorize]
[ApiController]
public sealed class AdminController(IAdminService service) : AdminControllerBase(service)
{
    [HttpPost("~/api/admin/setup/apply-defaults")]
    public Task<TradeSetupResult> ApplyDefaults(
        [FromServices] ITradeSetupService setupService,
        CancellationToken ct)
        => setupService.EnsureDefaultsAsync(ct);
}
