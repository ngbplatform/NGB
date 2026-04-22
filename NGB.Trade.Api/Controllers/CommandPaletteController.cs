using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Contracts.Search;
using NGB.Trade.Api.Services;

namespace NGB.Trade.Api.Controllers;

[Authorize]
[ApiController]
public sealed class CommandPaletteController(TradeCommandPaletteSearchService service) : ControllerBase
{
    [HttpPost("~/api/search/command-palette")]
    public Task<CommandPaletteSearchResponseDto> Search(
        [FromBody] CommandPaletteSearchRequestDto request,
        CancellationToken ct)
        => service.SearchAsync(request, ct);
}
