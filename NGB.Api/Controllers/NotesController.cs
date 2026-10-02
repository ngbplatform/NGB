using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Features;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Notes;
using NGB.Core.Features;
using NGB.Notes;

namespace NGB.Api.Controllers;

[Authorize, ApiController, Route("api/notes")]
[NgbFeature(NgbFeatures.Notes)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotesController(INoteService service) : ControllerBase
{
    [HttpGet]
    public Task<BusinessObjectPage<NoteDto>> List(
        [FromQuery] BusinessObjectKind kind,
        [FromQuery] string typeCode,
        [FromQuery] Guid objectId,
        [FromQuery] int limit = 50,
        [FromQuery] Guid? cursor = null,
        CancellationToken ct = default)
        => service.ListAsync(new(kind, typeCode, objectId), limit, cursor, ct);
    
    [HttpPost, RequestSizeLimit(1048576)]
    public Task<NoteDto> Create([FromBody] CreateNoteRequest request, CancellationToken ct)
        => service.CreateAsync(request, ct);
    
    [HttpPut("{id:guid}"), RequestSizeLimit(1048576)]
    public Task<NoteDto> Update(Guid id, [FromBody] UpdateNoteRequest request, CancellationToken ct)
        => service.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] long version, CancellationToken ct)
    {
        await service.DeleteAsync(id, version, ct);
        return NoContent();
    }
}
