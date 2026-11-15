using EquipmentInspection.Definitions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;

namespace EquipmentInspection.Api;

[Authorize]
[ApiController]
[Route("api/inspection-sources")]
public sealed class InspectionSourcesController(IInspectionSourceReader sources, INgbAccessChecker access) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<InspectionSource>> Search(
        [FromQuery] Guid equipmentId,
        [FromQuery] string query = "",
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        await RequireAccessAsync(ct);
        if (equipmentId == Guid.Empty || offset < 0 || offset > 10000 || limit < 1 || limit > 100 || query.Length > 200)
            throw new NgbArgumentInvalidException("source", "Select equipment and use a bounded source search.");

        return await sources.SearchAsync(equipmentId, query, offset, limit, ct);
    }

    [HttpGet("{id:guid}")]
    public async Task<InspectionSource> Preview(
        [FromRoute] Guid id,
        [FromQuery] Guid equipmentId,
        CancellationToken ct)
    {
        await RequireAccessAsync(ct);
        return await sources.ReadEligibleAsync(id, equipmentId, ct)
            ?? throw new InspectionSourceUnavailableException();
    }

    private async Task RequireAccessAsync(CancellationToken ct)
    {
        await access.RequireAsync("document", EquipmentDefinitions.InspectionCode, "view", ct);
        await access.RequireAsync("document", EquipmentDefinitions.InspectionCode, "create", ct);
    }
}
