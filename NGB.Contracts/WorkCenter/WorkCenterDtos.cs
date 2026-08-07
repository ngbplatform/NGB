using NGB.Core.WorkCenter;
using NGB.Contracts.Documents;

namespace NGB.Contracts.WorkCenter;

public sealed record WorkCenterSummaryDto(
    int AttentionCount,
    int OpenTaskCount,
    int OverdueTaskCount,
    int NotificationCount,
    int UnreadNotificationCount,
    long Version);

public sealed record WorkCenterSourceDto(
    string ResourceKind,
    string ResourceCode,
    Guid EntityId,
    string Title,
    string? Subtitle);

public sealed record WorkCenterAssignmentDto(
    Guid? AssignedUserId,
    Guid? AssignedRoleId,
    Guid? ClaimedByUserId,
    bool IsRoleAssigned);

public sealed record WorkCenterItemDto(
    Guid Id,
    WorkCenterItemKind Kind,
    string Code,
    string Title,
    string? Description,
    WorkCenterSourceDto Source,
    WorkCenterPriority? Priority,
    NotificationSeverity? Severity,
    WorkCenterTaskStatus? TaskStatus,
    DateTime SortAtUtc,
    DateTime? DueAtUtc,
    bool IsOverdue,
    bool IsRead,
    DateTime? SnoozedUntilUtc,
    WorkCenterAssignmentDto? Assignment,
    string? PrimaryActionCode,
    DocumentActionTargetDto? Target,
    long Version);

public sealed record WorkCenterPageDto(
    IReadOnlyList<WorkCenterItemDto> Items,
    string? NextCursor,
    int Limit);

public sealed record WorkCenterQueryDto(
    string? Cursor = null,
    int Limit = 30,
    string? Tab = null,
    string? Vertical = null,
    WorkCenterPriority? Priority = null,
    NotificationSeverity? Severity = null,
    bool? Overdue = null,
    bool? Unread = null);

public sealed record SnoozeWorkCenterTaskRequestDto(DateTime SnoozedUntilUtc);

public sealed record ClaimWorkCenterTaskRequestDto(long ExpectedVersion);

public sealed record NotificationPreferenceDto(
    string Code,
    WorkCenterPreferenceKind Kind,
    string DisplayName,
    string Category,
    NotificationChannel Channel,
    bool IsEnabled,
    bool DefaultEnabled,
    bool UserCanDisable,
    bool IsMandatory)
{
    public string? Description { get; init; }
}

public sealed record UpdateNotificationPreferenceDto(
    string Code,
    NotificationChannel Channel,
    bool IsEnabled);

public sealed record UpdateNotificationPreferencesRequestDto(IReadOnlyList<UpdateNotificationPreferenceDto> Preferences);
