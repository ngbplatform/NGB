using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Trade.Contracts;
using NGB.Trade.Runtime.Pricing;

namespace NGB.Trade.Api.Controllers;

[Authorize]
[ApiController]
public sealed class TradeDocumentLineDefaultsController(TradeDocumentLineDefaultsService service) : ControllerBase
{
    [HttpPost("~/api/trade/document-line-defaults/resolve")]
    public Task<TradeDocumentLineDefaultsResponseDto> Resolve(
        [FromBody] TradeDocumentLineDefaultsRequestDto request,
        CancellationToken ct)
        => service.ResolveAsync(request, ct);
}
