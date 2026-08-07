using NGB.Contracts.WorkCenter;
using NGB.Contracts.Documents;
using NGB.Application.Abstractions.IntegrationEvents;
using NGB.Core.Documents.Actions;
using NGB.Core.WorkCenter;
using NotificationChannel = NGB.Core.WorkCenter.NotificationChannel;
using NotificationSeverity = NGB.Core.WorkCenter.NotificationSeverity;
using WorkCenterPreferenceKind = NGB.Core.WorkCenter.WorkCenterPreferenceKind;
using WorkCenterPriority = NGB.Core.WorkCenter.WorkCenterPriority;

namespace NGB.Application.Abstractions.Services;

public sealed record WorkCenterPreferenceDefinition(
    string Code,
    WorkCenterPreferenceKind Kind,
    string DisplayName,
    string Category,
    bool DefaultEnabled,
    bool UserCanDisable,
    NotificationSeverity DefaultSeverity,
    IReadOnlySet<NotificationChannel> SupportedChannels,
    TimeSpan? Retention,
    string? LabelKey = null,
    bool IsMandatory = false,
    IReadOnlySet<string>? ApplicableRoleCodes = null)
{
    public string? Description { get; init; }
}

public interface IWorkCenterPreferenceDefinitionSource
{
    IReadOnlyList<WorkCenterPreferenceDefinition> GetDefinitions();
}

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

public interface IWorkCenterTaskService
{
    Task<Guid?> CreateAsync(CreateWorkCenterTaskRequest request, CancellationToken ct);
    Task CompleteByDeduplicationKeyAsync(string taskCode, string deduplicationKey, CancellationToken ct);
    Task CancelByDeduplicationKeyAsync(string taskCode, string deduplicationKey, CancellationToken ct);
}

public interface INotificationService
{
    Task<Guid?> CreateAsync(CreateNotificationRequest request, CancellationToken ct);
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
    Task HandleAsync(DocumentActionCompletedV1 @event, CancellationToken ct);
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

/// <summary>
/// Tracks recipients changed by the current scoped Work Center projection. The processor
/// drains the tracker only after the enclosing transaction commits and sends one coalesced
/// realtime invalidation per user instead of broadcasting every outbox event globally.
/// </summary>
public interface IWorkCenterChangeTracker
{
    void Track(IEnumerable<Guid> userIds);
    IReadOnlyList<Guid> Drain();
    void Reset();
}
