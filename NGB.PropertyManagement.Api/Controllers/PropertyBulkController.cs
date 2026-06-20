using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Core.Security;
using NGB.PropertyManagement.Contracts.Catalogs;
using NGB.PropertyManagement.Runtime.Catalogs;
using NGB.Runtime.Security;

namespace NGB.PropertyManagement.Api.Controllers;

[Authorize]
[ApiController]
public sealed class PropertyBulkController(INgbAccessChecker access) : ControllerBase
{
    [HttpPost("~/api/catalogs/pm.property/bulk-create-units")]
    public async Task<PropertyBulkCreateUnitsResponse> BulkCreateUnits(
        [FromServices] IPropertyBulkCreateUnitsService service,
        [FromBody] PropertyBulkCreateUnitsRequest request,
        [FromQuery] bool dryRun,
        CancellationToken ct)
    {
        await access.RequireAsync(
            NgbResourceKinds.Catalog,
            PropertyManagementCodes.Property,
            NgbPermissionActions.Edit,
            ct);

        return dryRun
            ? await service.DryRunAsync(request, ct)
            : await service.BulkCreateUnitsAsync(request, ct);
    }
}
