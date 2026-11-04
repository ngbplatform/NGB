using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Application.Abstractions.Features;
using NGB.Core.Features;
using NGB.Core.Security;
using NGB.Runtime.Security;

namespace EquipmentInspection.IntegrationTests;

[Authorize]
[ApiController]
[Route("api/example-access")]
public sealed class InspectionAccessController(INgbAccessChecker access, INgbFeatureService features) : ControllerBase
{
    [HttpGet("registered-permission")]
    public async Task<bool> RegisteredPermission(CancellationToken ct)
    {
        await access.RequireAsync("document", "example.inspection", "approve", ct);
        return await access.HasAsync("document", "example.inspection", "approve", ct);
    }

    [HttpGet("content-permissions")]
    public async Task<IReadOnlyList<bool>> ContentPermissions(CancellationToken ct)
    {
        NgbPermissionKey[] permissions =
        [
            NgbSystemPermissions.AttachmentsRead,
            NgbSystemPermissions.AttachmentsCreate,
            NgbSystemPermissions.AttachmentsDelete,
            NgbSystemPermissions.NotesRead,
            NgbSystemPermissions.NotesCreate,
            NgbSystemPermissions.NotesUpdate,
            NgbSystemPermissions.NotesDelete
        ];
        var results = new List<bool>();
        foreach (var permission in permissions)
            results.Add(await access.HasAsync(permission.ResourceKind, permission.ResourceCode, permission.ActionCode, ct));

        return results;
    }

    [HttpPost("disabled-feature")]
    public async Task<IActionResult> DisabledFeature(CancellationToken ct)
    {
        await access.RequireAsync("document", "example.inspection", "approve", ct);
        await features.RequireAsync(NgbFeatures.Attachments, ct);
        return NoContent();
    }
}
