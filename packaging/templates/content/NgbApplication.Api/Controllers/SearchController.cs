using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Application.Abstractions.Services;

namespace NgbApplication.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/application-search")]
public sealed class SearchController(CatalogCodeSearch search) : ControllerBase
{
    [HttpGet]
    public Task<NgbSearchResult> Search(
        [FromQuery] string query,
        [FromQuery] int limit = 20,
        [FromQuery] NgbSearchKind? kind = null,
        CancellationToken ct = default)
        => search.SearchAsync(query, limit, kind, null, ct);
}
