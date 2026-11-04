using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Application.Abstractions.Services;

namespace EquipmentInspection.Api;

[Authorize]
[ApiController]
[Route("api/equipment-search")]
public sealed class EquipmentSearchController(INgbSearchService search) : ControllerBase
{
    [HttpGet]
    public Task<NgbSearchResult> Search(
        [FromQuery] string query,
        [FromQuery] int limit = 20,
        [FromQuery] NgbSearchKind? kind = null,
        [FromQuery] string? typeCode = null,
        CancellationToken ct = default)
        => search.SearchAsync(query, limit, kind, typeCode, ct);
}
