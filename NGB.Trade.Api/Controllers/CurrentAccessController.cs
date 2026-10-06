using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Contracts.Security;
using NGB.Runtime.Security;

namespace NGB.Trade.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/security/me/access")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CurrentAccessController(ICurrentAccessService currentAccess) : ControllerBase
{
    [HttpGet]
    public Task<CurrentAccessDto> Get(CancellationToken ct) => currentAccess.GetCurrentAccessAsync(ct);
}
