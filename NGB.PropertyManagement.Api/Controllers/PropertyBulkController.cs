using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.PropertyManagement.Contracts.Catalogs;
using NGB.PropertyManagement.Runtime.Catalogs;

namespace NGB.PropertyManagement.Api.Controllers;

[Authorize]
[ApiController]
public sealed class PropertyBulkController : ControllerBase
{
    [HttpPost("~/api/catalogs/pm.property/bulk-create-units")]
    public Task<PropertyBulkCreateUnitsResponse> BulkCreateUnits(
        [FromServices] IPropertyBulkCreateUnitsService service,
        [FromBody] PropertyBulkCreateUnitsRequest request,
        [FromQuery] bool dryRun,
        CancellationToken ct)
        => dryRun
            ? service.DryRunAsync(request, ct)
            : service.BulkCreateUnitsAsync(request, ct);
}
