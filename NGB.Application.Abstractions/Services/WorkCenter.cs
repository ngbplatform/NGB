using NGB.Contracts.WorkCenter;
using NGB.Contracts.Documents;
using NGB.Contracts.IntegrationEvents;
using NGB.Core.Documents.Actions;
using NGB.Core.WorkCenter;
using NotificationSeverity = NGB.Core.WorkCenter.NotificationSeverity;
using WorkCenterPriority = NGB.Core.WorkCenter.WorkCenterPriority;

namespace NGB.Application.Abstractions.Services;

public sealed record CreateWorkCenterTaskRequest(
    string TaskCode,
    WorkCenterSourceReference Source,
    string Title,
    string? Description,
    WorkCenterPriority Priority,
    Guid? AssignedUserId,
    string? AssignedRoleCode,
    DateTime? DueAtUtc,
    DocumentActionCode? PrimaryActionCode,
    DocumentActionTargetDto? Target,
    string DeduplicationKey,
    Guid? CorrelationId,
    Guid? CausationId);

public sealed record CreateNotificationRequest(
    string DefinitionCode,
    WorkCenterSourceReference Source,
    string Title,
    string? Body,
    NotificationSeverity? Severity,
    IReadOnlyList<Guid> RecipientUserIds,
    DateTime? ExpiresAtUtc,
    string DeduplicationKey,
    Guid? CorrelationId,
    Guid? CausationId)
{
    /// <summary>
    /// Optional role-based recipient source. Exactly one recipient source is allowed.
    /// </summary>
    public string? RecipientRoleCode { get; init; }
}

/// <summary>
/// Explicit outcome of a Work Center mutation. Changed users are notified only after
/// the caller's transaction commits; no scoped mutable state crosses that boundary.
/// </summary>
public sealed record WorkCenterMutationResult(Guid? ItemId, IReadOnlyList<Guid> ChangedUserIds)
{
    public static WorkCenterMutationResult Empty { get; } = new(null, []);
}

public interface IWorkCenterTaskService
{
    Task<WorkCenterMutationResult> CreateAsync(CreateWorkCenterTaskRequest request, CancellationToken ct);

    Task<IReadOnlyList<Guid>> CompleteByDeduplicationKeyAsync(
        string taskCode,
        string deduplicationKey,
        CancellationToken ct);

    Task<IReadOnlyList<Guid>> CompleteByDeduplicationKeysAsync(
        string taskCode,
        IReadOnlyCollection<string> deduplicationKeys,
        CancellationToken ct);

    Task<IReadOnlyList<Guid>> CancelByDeduplicationKeyAsync(
        string taskCode,
        string deduplicationKey,
        CancellationToken ct);
}

public interface INotificationService
{
    Task<WorkCenterMutationResult> CreateAsync(CreateNotificationRequest request, CancellationToken ct);
}

public interface IWorkCenterQueryService
{
    Task<WorkCenterSummaryDto> GetSummaryAsync(string? vertical, CancellationToken ct);
    Task<WorkCenterPageDto> GetItemsAsync(WorkCenterQueryDto query, CancellationToken ct);
    Task MarkNotificationReadAsync(Guid notificationId, CancellationToken ct);
    Task DismissNotificationAsync(Guid notificationId, CancellationToken ct);
    Task MarkTaskReadAsync(Guid taskId, CancellationToken ct);
    Task ClaimTaskAsync(Guid taskId, long expectedVersion, CancellationToken ct);
    Task SnoozeTaskAsync(Guid taskId, DateTime snoozedUntilUtc, CancellationToken ct);
    Task<IReadOnlyList<NotificationPreferenceDto>> GetNotificationPreferencesAsync(CancellationToken ct);
    Task UpdateNotificationPreferencesAsync(UpdateNotificationPreferencesRequestDto request, CancellationToken ct);
}

public interface IDocumentActionCompletedWorkCenterPolicy
{
    Task<IReadOnlyList<Guid>> HandleAsync(DocumentActionCompletedV1 @event, CancellationToken ct);
}

public interface IOutboxProcessor
{
    Task<int> ProcessBatchAsync(int batchSize, CancellationToken ct);
}

public sealed record WorkCenterOperationalHealthSnapshot(
    long PendingDeliveryCount,
    long FailedDeliveryCount,
    double OldestPendingAgeSeconds,
    long OpenTaskCount,
    long OverdueTaskCount);

public interface IWorkCenterOperationalHealthReader
{
    Task<WorkCenterOperationalHealthSnapshot> ReadAsync(CancellationToken ct);
}

public interface IWorkCenterMaintenanceService
{
    Task<int> PruneAsync(CancellationToken ct);
}

public interface IWorkCenterRealtimeNotifier
{
    Task NotifyUsersChangedAsync(long version, IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}
