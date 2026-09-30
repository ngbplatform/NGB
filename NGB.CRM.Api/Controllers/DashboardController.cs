using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Core.Security;
using NGB.CRM.Contracts.Dashboard;
using NGB.Runtime.Security;

namespace NGB.CRM.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(ICrmDashboardService service, INgbAccessChecker access) : ControllerBase
{
    [HttpGet]
    public async Task<CrmDashboardResponse> Get([FromQuery] DateOnly asOfUtc, CancellationToken ct)
    {
        await access.RequireAsync(
            NgbResourceKinds.Page,
            CrmCodes.Dashboard,
            NgbPermissionActions.View,
            ct);

        return await service.GetAsync(asOfUtc, ct);
    }
}
