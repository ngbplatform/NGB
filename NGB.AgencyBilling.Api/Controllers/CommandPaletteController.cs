using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Contracts.Search;
using NGB.AgencyBilling.Api.Services;

namespace NGB.AgencyBilling.Api.Controllers;

[Authorize]
[ApiController]
public sealed class CommandPaletteController(AgencyBillingCommandPaletteSearchService service) : ControllerBase
{
    [HttpPost("~/api/search/command-palette")]
    public Task<CommandPaletteSearchResponseDto> Search(
        [FromBody] CommandPaletteSearchRequestDto request,
        CancellationToken ct)
        => service.SearchAsync(request, ct);
}
