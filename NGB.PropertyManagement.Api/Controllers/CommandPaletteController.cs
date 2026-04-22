using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Contracts.Search;
using NGB.PropertyManagement.Api.Services;

namespace NGB.PropertyManagement.Api.Controllers;

[Authorize]
[ApiController]
public sealed class CommandPaletteController(ICommandPaletteSearchService service) : ControllerBase
{
    [HttpPost("~/api/search/command-palette")]
    public Task<CommandPaletteSearchResponseDto> Search(
        [FromBody] CommandPaletteSearchRequestDto request,
        CancellationToken ct)
        => service.SearchAsync(request, ct);
}
