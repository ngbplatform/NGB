using NGB.Core.WorkCenter;

namespace NGB.Persistence.WorkCenter;

public sealed record WorkCenterCursor(DateTime SortAtUtc, Guid Id);

public enum WorkCenterQueryView
{
    Attention = 1,
    Tasks = 2,
    Notifications = 3,
    Completed = 4
}

public sealed record WorkCenterQuery(
    Guid UserId,
    IReadOnlyList<Guid> RoleIds,
    bool AllowAllSources,
    IReadOnlyList<string> AllowedResourceKinds,
    IReadOnlyList<string> AllowedResourceCodes,
    WorkCenterCursor? Cursor,
    int Limit,
    WorkCenterQueryView View,
    string? Vertical,
    WorkCenterPriority? Priority,
    NotificationSeverity? Severity,
    bool? Overdue,
    bool? Unread,
    DateTime NowUtc);

public sealed record WorkCenterNavigationTargetRecord(string Code, IReadOnlyDictionary<string, string?> Parameters);

public sealed record WorkCenterItemRecord(
    Guid Id,
    WorkCenterItemKind Kind,
    string Code,
    string Title,
    string? Description,
    string SourceResourceKind,
    string SourceResourceCode,
    Guid SourceEntityId,
    string SourceTitleSnapshot,
    string? SourceSubtitleSnapshot,
    WorkCenterPriority? Priority,
    NotificationSeverity? Severity,
    WorkCenterTaskStatus? TaskStatus,
    DateTime SortAtUtc,
    DateTime? DueAtUtc,
    bool IsRead,
    DateTime? SnoozedUntilUtc,
    Guid? AssignedUserId,
    Guid? AssignedRoleId,
    Guid? ClaimedByUserId,
    string? PrimaryActionCode,
    WorkCenterNavigationTargetRecord? Target,
    long Version);

public sealed record WorkCenterSummaryRecord(
    int AttentionCount,
    int OpenTaskCount,
    int OverdueTaskCount,
    int NotificationCount,
    int UnreadNotificationCount,
    long Version);

public sealed record WorkCenterTaskHealthRecord(long OpenTaskCount, long OverdueTaskCount);

public sealed record WorkCenterTaskCreateResult(Guid TaskId, bool BecameActive, long Version);
public sealed record WorkCenterTaskMutationResult(bool Changed, IReadOnlyList<Guid> RecipientUserIds);
public sealed record WorkCenterNotificationCreateResult(Guid NotificationId, IReadOnlyList<Guid> CreatedRecipientUserIds);

public interface IWorkCenterTaskRepository
{
    Task<WorkCenterTaskCreateResult> CreateAsync(
        WorkCenterTask task,
        string? primaryActionCode,
        WorkCenterNavigationTargetRecord? target,
        IReadOnlyList<Guid> recipientUserIds,
        CancellationToken ct);

    Task<WorkCenterTaskMutationResult> CompleteByDeduplicationKeyAsync(
        string taskCode,
        string deduplicationKey,
        DateTime completedAtUtc,
        CancellationToken ct);

    Task<WorkCenterTaskMutationResult> CancelByDeduplicationKeyAsync(
        string taskCode,
        string deduplicationKey,
        DateTime cancelledAtUtc,
        CancellationToken ct);

    Task MarkReadAsync(
        Guid taskId,
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        bool allowAllSources,
        IReadOnlyList<string> allowedResourceKinds,
        IReadOnlyList<string> allowedResourceCodes,
        DateTime readAtUtc,
        CancellationToken ct);

    Task SnoozeAsync(
        Guid taskId,
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        bool allowAllSources,
        IReadOnlyList<string> allowedResourceKinds,
        IReadOnlyList<string> allowedResourceCodes,
        DateTime snoozedUntilUtc,
        CancellationToken ct);

    Task<bool> ClaimAsync(
        Guid taskId,
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        bool allowAllSources,
        IReadOnlyList<string> allowedResourceKinds,
        IReadOnlyList<string> allowedResourceCodes,
        long expectedVersion,
        DateTime claimedAtUtc,
        CancellationToken ct);
}

public interface INotificationRepository
{
    Task<WorkCenterNotificationCreateResult> CreateAsync(
        WorkCenterNotification notification,
        IReadOnlyList<Guid> recipientUserIds,
        CancellationToken ct);

    Task MarkReadAsync(
        Guid notificationId,
        Guid userId,
        bool allowAllSources,
        IReadOnlyList<string> allowedResourceKinds,
        IReadOnlyList<string> allowedResourceCodes,
        DateTime readAtUtc,
        CancellationToken ct);

    Task DismissAsync(
        Guid notificationId,
        Guid userId,
        bool allowAllSources,
        IReadOnlyList<string> allowedResourceKinds,
        IReadOnlyList<string> allowedResourceCodes,
        DateTime dismissedAtUtc,
        CancellationToken ct);
}

public sealed record NotificationPreferenceRecord(
    Guid UserId,
    string NotificationCode,
    NotificationChannel Channel,
    bool IsEnabled,
    DateTime UpdatedAtUtc,
    long Version);

public interface INotificationPreferenceRepository
{
    Task<IReadOnlyList<NotificationPreferenceRecord>> GetForUserAsync(Guid userId, CancellationToken ct);

    Task<IReadOnlyList<NotificationPreferenceRecord>> GetForUsersAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken ct);

    Task UpsertAsync(NotificationPreferenceRecord preference, CancellationToken ct);

    Task UpsertManyAsync(IReadOnlyList<NotificationPreferenceRecord> preferences, CancellationToken ct);
}

public interface IWorkCenterReadRepository
{
    Task<WorkCenterTaskHealthRecord> GetTaskHealthAsync(DateTime nowUtc, CancellationToken ct);

    Task<WorkCenterSummaryRecord> GetSummaryAsync(
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        bool allowAllSources,
        IReadOnlyList<string> allowedResourceKinds,
        IReadOnlyList<string> allowedResourceCodes,
        string? vertical,
        DateTime nowUtc,
        CancellationToken ct);

    Task<IReadOnlyList<WorkCenterItemRecord>> GetItemsAsync(WorkCenterQuery query, CancellationToken ct);
}

public sealed record WorkCenterRetentionCutoffs(
    DateTime DocumentActionExecutionsBeforeUtc,
    DateTime TerminalTasksBeforeUtc,
    DateTime NotificationDeliveriesBeforeUtc,
    DateTime OutboxBeforeUtc);

public sealed record WorkCenterPruneResult(
    int DocumentActionExecutions,
    int Tasks,
    int NotificationDeliveries,
    int Notifications,
    int OutboxEvents)
{
    public int Total => DocumentActionExecutions + Tasks + NotificationDeliveries + Notifications + OutboxEvents;
}

public interface IWorkCenterMaintenanceRepository
{
    Task<WorkCenterPruneResult> PruneAsync(
        WorkCenterRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken ct);
}
