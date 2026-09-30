using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Core.Security;
using NGB.PropertyManagement.Contracts.Dashboard;
using NGB.PropertyManagement.Definitions;
using NGB.Runtime.Security;

namespace NGB.PropertyManagement.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(IPropertyManagementDashboardService service, INgbAccessChecker access)
    : ControllerBase
{
    [HttpGet]
    public async Task<PropertyManagementDashboardResponse> Get([FromQuery] DateOnly asOfUtc, CancellationToken ct)
    {
        await access.RequireAsync(
            NgbResourceKinds.Page,
            PropertyManagementSecurityDefaults.HomePage,
            NgbPermissionActions.View,
            ct);

        return await service.GetAsync(asOfUtc, ct);
    }
}
