using NGB.Core.Documents.Actions;

namespace NGB.Core.WorkCenter;

public sealed record WorkCenterSourceReference(
    string ResourceKind,
    string ResourceCode,
    Guid EntityId,
    string TitleSnapshot,
    string? SubtitleSnapshot);

public sealed record WorkCenterTask(
    Guid Id,
    string TaskCode,
    string? PreferenceCode,
    WorkCenterSourceReference Source,
    string Title,
    string? Description,
    WorkCenterPriority Priority,
    WorkCenterTaskStatus Status,
    Guid? AssignedUserId,
    Guid? AssignedRoleId,
    Guid? ClaimedByUserId,
    DateTime? DueAtUtc,
    DocumentActionCode? PrimaryActionCode,
    string? NavigationTargetCode,
    IReadOnlyDictionary<string, string?> NavigationParameters,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancelledAtUtc,
    string DeduplicationKey,
    long Version,
    Guid? CorrelationId,
    Guid? CausationId,
    string? MetadataJson);

public sealed record WorkCenterNotification(
    Guid Id,
    string DefinitionCode,
    WorkCenterSourceReference Source,
    string Title,
    string? Body,
    NotificationSeverity Severity,
    DateTime CreatedAtUtc,
    DateTime? ExpiresAtUtc,
    string DeduplicationKey,
    Guid? CorrelationId,
    Guid? CausationId,
    string? MetadataJson);
