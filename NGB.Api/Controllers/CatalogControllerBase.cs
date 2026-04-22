using Microsoft.AspNetCore.Mvc;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Application.Abstractions.Services;
using NGB.Api.Internal;
using NGB.Contracts.Services;

namespace NGB.Api.Controllers;

public abstract class CatalogControllerBase(ICatalogService service) : ControllerBase
{
    [HttpGet("~/api/catalogs/metadata")]
    public Task<IReadOnlyList<CatalogTypeMetadataDto>> GetAllMetadata(CancellationToken ct)
        => service.GetAllMetadataAsync(ct);

    [HttpGet("~/api/catalogs/{catalogType}/metadata")]
    public Task<CatalogTypeMetadataDto> GetTypeMetadata([FromRoute] string catalogType, CancellationToken ct)
        => service.GetTypeMetadataAsync(catalogType, ct);

    [HttpGet]
    public Task<PageResponseDto<CatalogItemDto>> GetPage([FromRoute] string catalogType, CancellationToken ct)
        => service.GetPageAsync(catalogType, QueryParsing.ToPageRequest(Request.Query), ct);

    [HttpGet("{id:guid}")]
    public Task<CatalogItemDto> GetById([FromRoute] string catalogType, [FromRoute] Guid id, CancellationToken ct)
        => service.GetByIdAsync(catalogType, id, ct);

    [HttpPost]
    public Task<CatalogItemDto> Create(
        [FromRoute] string catalogType,
        [FromBody] RecordPayload payload,
        CancellationToken ct)
        => service.CreateAsync(catalogType, payload, ct);

    [HttpPut("{id:guid}")]
    public Task<CatalogItemDto> Update(
        [FromRoute] string catalogType,
        [FromRoute] Guid id,
        [FromBody] RecordPayload payload,
        CancellationToken ct)
        => service.UpdateAsync(catalogType, id, payload, ct);

    [HttpPost("{id:guid}/mark-for-deletion")]
    public async Task<IActionResult> MarkForDeletion(
        [FromRoute] string catalogType,
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        await service.MarkForDeletionAsync(catalogType, id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/unmark-for-deletion")]
    public async Task<IActionResult> UnmarkForDeletion(
        [FromRoute] string catalogType,
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        await service.UnmarkForDeletionAsync(catalogType, id, ct);
        return NoContent();
    }

    [HttpGet("lookup")]
    public Task<IReadOnlyList<LookupItemDto>> Lookup(
        [FromRoute] string catalogType,
        [FromQuery(Name = "q")] string? q,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
        => service.LookupAsync(catalogType, q, limit, ct);

    [HttpPost("by-ids")]
    public Task<IReadOnlyList<LookupItemDto>> GetByIds(
        [FromRoute] string catalogType,
        [FromBody] ByIdsRequestDto request,
        CancellationToken ct)
        => service.GetByIdsAsync(catalogType, request.Ids, ct);
}
