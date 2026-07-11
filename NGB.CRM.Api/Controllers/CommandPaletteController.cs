using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Contracts.Search;
using NGB.CRM.Api.Services;

namespace NGB.CRM.Api.Controllers;

[Authorize]
[ApiController]
public sealed class CommandPaletteController(CrmCommandPaletteSearchService service) : ControllerBase
{
    [HttpPost("~/api/search/command-palette")]
    public Task<CommandPaletteSearchResponseDto> Search(
        [FromBody] CommandPaletteSearchRequestDto request,
        CancellationToken ct)
        => service.SearchAsync(request, ct);
}
