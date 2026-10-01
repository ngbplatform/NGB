using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Attachments;
using NGB.Contracts.Attachments;
using NGB.Contracts.BusinessObjects;

namespace NGB.Api.Controllers;

[Authorize, ApiController, Route("api/attachments")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AttachmentsController(IAttachmentService service) : ControllerBase
{
    [HttpGet]
    public Task<BusinessObjectPage<AttachmentDto>> List(
        [FromQuery] BusinessObjectKind kind,
        [FromQuery] string typeCode,
        [FromQuery] Guid objectId,
        [FromQuery] int limit = 50,
        [FromQuery] Guid? cursor = null,
        CancellationToken ct = default)
        => service.ListAsync(new(kind, typeCode, objectId), limit, cursor, ct);

    [HttpPost("uploads"), RequestSizeLimit(16384)]
    public Task<AttachmentUploadDto> CreateUpload(
        [FromBody] CreateAttachmentUploadRequest request,
        CancellationToken ct)
        => service.CreateUploadAsync(request, ct);

    [HttpPost("{id:guid}/complete")]
    public Task<AttachmentDto> Complete(Guid id, CancellationToken ct) => service.CompleteAsync(id, ct);

    [HttpPost("{id:guid}/download")]
    public Task<AttachmentDownloadDto> Download(Guid id, CancellationToken ct) => service.DownloadAsync(id, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}
