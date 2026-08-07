using NGB.Contracts.WorkCenter;
using NGB.Core.Events;
using NGB.Core.WorkCenter;

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
    string? PrimaryActionCode,
    string? NavigationTargetCode,
    IReadOnlyDictionary<string, string?> NavigationParameters,
    string DeduplicationKey,
    Guid? CorrelationId,
    Guid? CausationId,
    string? MetadataJson);

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
    Guid? CausationId,
    string? MetadataJson);

public interface IWorkCenterTaskService
{
    Task<Guid?> CreateAsync(CreateWorkCenterTaskRequest request, CancellationToken ct);
    Task CompleteByDeduplicationKeyAsync(string deduplicationKey, CancellationToken ct);
    Task CancelByDeduplicationKeyAsync(string deduplicationKey, CancellationToken ct);
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

public sealed record WorkCenterEventContext(PlatformOutboxEvent Event);

public interface IWorkCenterEventPolicy
{
    string EventType { get; }

    Task HandleAsync(WorkCenterEventContext context, CancellationToken ct);
}

public interface IOutboxProcessor
{
    Task<int> ProcessBatchAsync(int batchSize, CancellationToken ct);
}

public interface IWorkCenterRealtimeNotifier
{
    Task NotifyChangedAsync(long version, CancellationToken ct);
}
