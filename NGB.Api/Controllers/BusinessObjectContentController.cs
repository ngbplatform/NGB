using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Api.Features;
using NGB.Application.Abstractions.BusinessObjects;
using NGB.Contracts.BusinessObjects;
using NGB.Core.Features;

namespace NGB.Api.Controllers;

[Authorize, ApiController, Route("api/business-objects/content-summary")]
[NgbFeature(NgbFeatures.Attachments, NgbFeatures.Notes)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BusinessObjectContentController(IBusinessObjectContentSummaryService service) : ControllerBase
{
    [HttpGet]
    public Task<BusinessObjectContentSummary> Get(
        [FromQuery] BusinessObjectKind kind,
        [FromQuery] string typeCode,
        [FromQuery] Guid objectId,
        CancellationToken ct)
        => service.GetAsync(new(kind, typeCode, objectId), ct);
}
