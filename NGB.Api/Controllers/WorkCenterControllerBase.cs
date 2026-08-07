using Microsoft.AspNetCore.Mvc;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.WorkCenter;

namespace NGB.Api.Controllers;

public abstract class WorkCenterControllerBase(IWorkCenterQueryService workCenter) : ControllerBase
{
    [HttpGet("~/api/work-center/summary")]
    public Task<WorkCenterSummaryDto> GetSummary([FromQuery] string? vertical, CancellationToken ct)
        => workCenter.GetSummaryAsync(vertical, ct);

    [HttpGet("~/api/work-center/items")]
    public Task<WorkCenterPageDto> GetItems([FromQuery] WorkCenterQueryDto query, CancellationToken ct)
        => workCenter.GetItemsAsync(query, ct);

    [HttpPost("~/api/work-center/notifications/{id:guid}/read")]
    public async Task<IActionResult> MarkNotificationRead([FromRoute] Guid id, CancellationToken ct)
    {
        await workCenter.MarkNotificationReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("~/api/work-center/notifications/{id:guid}/dismiss")]
    public async Task<IActionResult> DismissNotification([FromRoute] Guid id, CancellationToken ct)
    {
        await workCenter.DismissNotificationAsync(id, ct);
        return NoContent();
    }

    [HttpPost("~/api/work-center/tasks/{id:guid}/read")]
    public async Task<IActionResult> MarkTaskRead([FromRoute] Guid id, CancellationToken ct)
    {
        await workCenter.MarkTaskReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("~/api/work-center/tasks/{id:guid}/claim")]
    public async Task<IActionResult> ClaimTask(
        [FromRoute] Guid id,
        [FromBody] ClaimWorkCenterTaskRequestDto request,
        CancellationToken ct)
    {
        await workCenter.ClaimTaskAsync(id, request.ExpectedVersion, ct);
        return NoContent();
    }

    [HttpPost("~/api/work-center/tasks/{id:guid}/snooze")]
    public async Task<IActionResult> SnoozeTask(
        [FromRoute] Guid id,
        [FromBody] SnoozeWorkCenterTaskRequestDto request,
        CancellationToken ct)
    {
        await workCenter.SnoozeTaskAsync(id, request.SnoozedUntilUtc, ct);
        return NoContent();
    }

    [HttpGet("~/api/me/notification-preferences")]
    public Task<IReadOnlyList<NotificationPreferenceDto>> GetPreferences(CancellationToken ct)
        => workCenter.GetNotificationPreferencesAsync(ct);

    [HttpPut("~/api/me/notification-preferences")]
    public async Task<IActionResult> UpdatePreferences(
        [FromBody] UpdateNotificationPreferencesRequestDto request,
        CancellationToken ct)
    {
        await workCenter.UpdateNotificationPreferencesAsync(request, ct);
        return NoContent();
    }
}
