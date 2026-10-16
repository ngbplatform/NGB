using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Contracts.Admin;
using NGB.Runtime.Admin;

namespace CertificationApp.Api.Controllers;

[Authorize]
[ApiController]
public sealed class MenuController(PermissionAwareAdminService service) : ControllerBase
{
    [HttpGet("api/main-menu")]
    public Task<MainMenuDto> Get(CancellationToken ct) => service.GetMainMenuAsync(ct);
}
