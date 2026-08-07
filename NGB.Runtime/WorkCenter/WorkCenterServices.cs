using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.WorkCenter;
using NGB.Core.Documents.Actions;
using NGB.Core.Security;
using NGB.Core.WorkCenter;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.WorkCenter;
using NGB.Runtime.Security;
using NGB.Runtime.Observability;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.WorkCenter;

internal sealed class WorkCenterPreferenceRecipientResolver(
    INotificationPreferenceRepository preferences,
    IPlatformUserRepository users,
    IPlatformUserRoleRepository userRoles,
    WorkCenterPreferenceDefinitionRegistry definitions)
{
    public async Task<IReadOnlyList<Guid>> ResolveAsync(
        string preferenceCode,
        WorkCenterPreferenceKind expectedKind,
        IReadOnlyList<Guid> candidateUserIds,
        CancellationToken ct)
    {
        var recipients = candidateUserIds
            .Where(static x => x != Guid.Empty)
            .Distinct()
            .ToArray();

        if (recipients.Length == 0)
            return [];

        var recipientUsers = await users.GetByIdsAsync(recipients, ct);
        recipients = recipients
            .Where(userId => recipientUsers.TryGetValue(userId, out var user) && user.IsActive)
            .ToArray();

        if (recipients.Length == 0)
            return recipients;

        var definition = definitions.Get(preferenceCode);
        if (definition.Kind != expectedKind)
        {
            throw new NgbConfigurationViolationException(
                $"Work Center preference definition '{definition.Code}' is registered as " +
                $"'{definition.Kind}' but '{expectedKind}' is required.");
        }

        if (definition.ApplicableRoleCodes is { Count: > 0 })
        {
            var rolesByUser = await userRoles.GetRolesForUsersAsync(recipients, ct);
            recipients = recipients
                .Where(userId => rolesByUser.TryGetValue(userId, out var assignedRoles)
                                 && assignedRoles.Any(role =>
                                     role.IsActive
                                     && definition.ApplicableRoleCodes.Contains(role.Code)))
                .ToArray();

            if (recipients.Length == 0)
                return [];
        }

        var configuredPreferences = await preferences.GetForUsersAsync(recipients, ct);

        return recipients
            .Where(userId =>
            {
                var configured = configuredPreferences.FirstOrDefault(
                    x => x.UserId == userId
                         && string.Equals(x.NotificationCode, definition.Code, StringComparison.OrdinalIgnoreCase)
                         && x.Channel == NotificationChannel.InApp);
                return definition.IsMandatory
                       || configured?.IsEnabled == true
                       || (configured is null && definition.DefaultEnabled);
            })
            .ToArray();
    }
}

internal sealed class WorkCenterTaskService(
    IUnitOfWork uow,
    IWorkCenterTaskRepository tasks,
    IPlatformRoleRepository roles,
    IPlatformUserRoleRepository userRoles,
    WorkCenterPreferenceRecipientResolver recipientResolver,
    TimeProvider timeProvider)
    : IWorkCenterTaskService
{
    public Task<Guid?> CreateAsync(CreateWorkCenterTaskRequest request, CancellationToken ct)
        => InTransactionAsync(async innerCt =>
        {
            ArgumentNullException.ThrowIfNull(request);
            Guid? roleId = null;

            if (request.AssignedRoleCode is not null)
            {
                var role = await roles.GetByCodeAsync(request.AssignedRoleCode, innerCt);
                if (role is null || !role.IsActive)
                {
                    throw new NgbConfigurationViolationException(
                        $"Work Center assignment role '{request.AssignedRoleCode}' is not registered or active.");
                }

                roleId = role.RoleId;
            }

            if (request.AssignedUserId.HasValue == roleId.HasValue)
                throw new NgbArgumentInvalidException(
                    "assignment",
                    "Exactly one assigned user or role code is required.");

            var candidateRecipients = request.AssignedUserId is { } assignedUserId
                ? [assignedUserId]
                : await userRoles.GetUserIdsForRoleAsync(roleId!.Value, innerCt);

            var recipients = await recipientResolver.ResolveAsync(
                request.TaskCode,
                WorkCenterPreferenceKind.Task,
                candidateRecipients,
                innerCt);

            if (recipients.Count == 0)
                return null;

            var now = timeProvider.GetUtcNowDateTime();
            var actionCode = request.PrimaryActionCode is null
                ? (DocumentActionCode?)null
                : new DocumentActionCode(request.PrimaryActionCode);

            var result = await tasks.CreateAsync(
                new WorkCenterTask(
                    Guid.CreateVersion7(),
                    request.TaskCode,
                    request.TaskCode,
                    request.Source,
                    request.Title,
                    request.Description,
                    request.Priority,
                    WorkCenterTaskStatus.Open,
                    request.AssignedUserId,
                    roleId,
                    ClaimedByUserId: null,
                    request.DueAtUtc,
                    actionCode,
                    request.NavigationTargetCode,
                    request.NavigationParameters,
                    now,
                    CompletedAtUtc: null,
                    CancelledAtUtc: null,
                    request.DeduplicationKey,
                    Version: 1,
                    request.CorrelationId,
                    request.CausationId,
                    request.MetadataJson),
                recipients,
                innerCt);

            return (Guid?)result.TaskId;
        }, ct);

    public Task CompleteByDeduplicationKeyAsync(string deduplicationKey, CancellationToken ct)
        => InTransactionAsync(
            innerCt => tasks.CompleteByDeduplicationKeyAsync(
                deduplicationKey,
                timeProvider.GetUtcNowDateTime(),
                innerCt),
            ct);

    public Task CancelByDeduplicationKeyAsync(string deduplicationKey, CancellationToken ct)
        => InTransactionAsync(
            innerCt => tasks.CancelByDeduplicationKeyAsync(
                deduplicationKey,
                timeProvider.GetUtcNowDateTime(),
                innerCt),
            ct);

    private Task InTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct)
        => uow.ExecuteInUowTransactionAsync(!uow.HasActiveTransaction, action, ct);

    private Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => uow.ExecuteInUowTransactionAsync(!uow.HasActiveTransaction, action, ct);
}

internal sealed class NotificationService(
    IUnitOfWork uow,
    INotificationRepository notifications,
    WorkCenterPreferenceRecipientResolver recipientResolver,
    WorkCenterPreferenceDefinitionRegistry definitions,
    TimeProvider timeProvider)
    : INotificationService
{
    public Task<Guid?> CreateAsync(CreateNotificationRequest request, CancellationToken ct)
        => uow.ExecuteInUowTransactionAsync<Guid?>(!uow.HasActiveTransaction, async innerCt =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var definition = definitions.Get(request.DefinitionCode);

            if (definition.Kind != WorkCenterPreferenceKind.Notification)
            {
                throw new NgbConfigurationViolationException(
                    $"Work Center preference definition '{definition.Code}' is registered as " +
                    $"'{definition.Kind}' and cannot create a notification.");
            }

            var enabledRecipients = await recipientResolver.ResolveAsync(
                request.DefinitionCode,
                WorkCenterPreferenceKind.Notification,
                request.RecipientUserIds,
                innerCt);

            if (enabledRecipients.Count == 0)
                return null;

            var now = timeProvider.GetUtcNowDateTime();
            var expires = request.ExpiresAtUtc
                ?? (definition.Retention is { } retention ? now.Add(retention) : null);

            var notificationId = await notifications.CreateAsync(
                new WorkCenterNotification(
                    Guid.CreateVersion7(),
                    definition.Code,
                    request.Source,
                    request.Title,
                    request.Body,
                    request.Severity ?? definition.DefaultSeverity,
                    now,
                    expires,
                    request.DeduplicationKey,
                    request.CorrelationId,
                    request.CausationId,
                    request.MetadataJson),
                enabledRecipients,
                innerCt);

            NgbFeatureTelemetry.WorkCenterNotificationsCreated.Add(
                1,
                new KeyValuePair<string, object?>("notification.code", definition.Code),
                new KeyValuePair<string, object?>("notification.severity", request.Severity ?? definition.DefaultSeverity));

            return notificationId;
        }, ct);
}

internal sealed class WorkCenterQueryService(
    IUnitOfWork uow,
    IWorkCenterReadRepository reads,
    IWorkCenterTaskRepository tasks,
    INotificationRepository notifications,
    INotificationPreferenceRepository preferences,
    IPlatformUserRoleRepository userRoles,
    IPermissionSnapshotProvider snapshots,
    WorkCenterPreferenceDefinitionRegistry definitions,
    TimeProvider timeProvider,
    IWorkCenterRealtimeNotifier realtime)
    : IWorkCenterQueryService
{
    public async Task<WorkCenterSummaryDto> GetSummaryAsync(string? vertical, CancellationToken ct)
    {
        using var activity = NgbFeatureTelemetry.Activities.StartActivity("work_center.summary.query");
        var started = Stopwatch.GetTimestamp();

        try
        {
            var access = await GetAccessAsync(ct);
            var visibility = GetVisibility(access.Snapshot);

            var row = await reads.GetSummaryAsync(
                access.UserId,
                access.RoleIds,
                visibility.AllowAll,
                visibility.ResourceKinds,
                visibility.ResourceCodes,
                vertical,
                timeProvider.GetUtcNowDateTime(),
                ct);

            activity?.SetStatus(ActivityStatusCode.Ok);

            return new WorkCenterSummaryDto(
                row.AttentionCount,
                row.OpenTaskCount,
                row.OverdueTaskCount,
                row.NotificationCount,
                row.UnreadNotificationCount,
                row.Version);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
        finally
        {
            NgbFeatureTelemetry.WorkCenterQueryDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("query.kind", "summary"));
        }
    }

    public async Task<WorkCenterPageDto> GetItemsAsync(WorkCenterQueryDto query, CancellationToken ct)
    {
        using var activity = NgbFeatureTelemetry.Activities.StartActivity("work_center.feed.query");
        var started = Stopwatch.GetTimestamp();

        try
        {
            ArgumentNullException.ThrowIfNull(query);
            var access = await GetAccessAsync(ct);
            var limit = Math.Clamp(query.Limit, 1, 100);
            var now = timeProvider.GetUtcNowDateTime();
            var cursor = DecodeCursor(query.Cursor);
            var visibility = GetVisibility(access.Snapshot);

            var rows = await reads.GetItemsAsync(
                new WorkCenterQuery(
                    access.UserId,
                    access.RoleIds,
                    visibility.AllowAll,
                    visibility.ResourceKinds,
                    visibility.ResourceCodes,
                    cursor,
                    limit + 1,
                    query.Tab,
                    query.Vertical,
                    query.Priority,
                    query.Severity,
                    query.Overdue,
                    query.Unread,
                    now),
                ct);

            // Permission snapshot is already materialized. This is a batched in-memory check by
            // distinct resource key and never performs a query per item.
            var allowedKeys = rows
                .Select(static x => (x.SourceResourceKind, x.SourceResourceCode))
                .Distinct()
                .ToDictionary(
                    static x => x,
                    x => access.Snapshot.Has(
                        x.SourceResourceKind,
                        x.SourceResourceCode,
                        NgbPermissionActions.View));

            var visible = rows
                .Where(x => allowedKeys[(x.SourceResourceKind, x.SourceResourceCode)])
                .ToArray();

            var hasMore = visible.Length > limit;
            var page = visible.Take(limit)
                .Select(x => ToDto(x, now))
                .ToArray();

            var nextCursor = hasMore && page.Length > 0
                ? EncodeCursor(new WorkCenterCursor(page[^1].SortAtUtc, page[^1].Id))
                : null;

            activity?.SetStatus(ActivityStatusCode.Ok);

            return new WorkCenterPageDto(page, nextCursor, limit);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
        finally
        {
            NgbFeatureTelemetry.WorkCenterQueryDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("query.kind", "feed"));
        }
    }

    public Task MarkNotificationReadAsync(Guid notificationId, CancellationToken ct)
        => MutateAsync((access, now, innerCt) =>
        {
            var visibility = GetVisibility(access.Snapshot);

            return notifications.MarkReadAsync(
                notificationId,
                access.UserId,
                visibility.AllowAll,
                visibility.ResourceKinds,
                visibility.ResourceCodes,
                now,
                innerCt);
        }, ct);

    public Task DismissNotificationAsync(Guid notificationId, CancellationToken ct)
        => MutateAsync((access, now, innerCt) =>
        {
            var visibility = GetVisibility(access.Snapshot);

            return notifications.DismissAsync(
                notificationId,
                access.UserId,
                visibility.AllowAll,
                visibility.ResourceKinds,
                visibility.ResourceCodes,
                now,
                innerCt);
        }, ct);

    public Task MarkTaskReadAsync(Guid taskId, CancellationToken ct)
        => MutateAsync((access, now, innerCt) =>
        {
            var visibility = GetVisibility(access.Snapshot);

            return tasks.MarkReadAsync(
                taskId,
                access.UserId,
                access.RoleIds,
                visibility.AllowAll,
                visibility.ResourceKinds,
                visibility.ResourceCodes,
                now,
                innerCt);
        }, ct);

    public Task ClaimTaskAsync(Guid taskId, long expectedVersion, CancellationToken ct)
        => MutateAsync(async (access, now, innerCt) =>
        {
            var visibility = GetVisibility(access.Snapshot);
            var claimed = await tasks.ClaimAsync(
                taskId,
                access.UserId,
                access.RoleIds,
                visibility.AllowAll,
                visibility.ResourceKinds,
                visibility.ResourceCodes,
                expectedVersion,
                now,
                innerCt);

            if (!claimed)
                throw new WorkCenterTaskClaimConflictException(taskId, expectedVersion);
        }, ct);

    public Task SnoozeTaskAsync(Guid taskId, DateTime snoozedUntilUtc, CancellationToken ct)
    {
        snoozedUntilUtc.EnsureUtc(nameof(snoozedUntilUtc));

        return MutateAsync((access, _, innerCt) =>
        {
            var visibility = GetVisibility(access.Snapshot);

            return tasks.SnoozeAsync(
                taskId,
                access.UserId,
                access.RoleIds,
                visibility.AllowAll,
                visibility.ResourceKinds,
                visibility.ResourceCodes,
                snoozedUntilUtc,
                innerCt);
        }, ct);
    }

    public async Task<IReadOnlyList<NotificationPreferenceDto>> GetNotificationPreferencesAsync(CancellationToken ct)
    {
        var access = await GetAccessAsync(ct);
        var configured = await preferences.GetForUserAsync(access.UserId, ct);

        return definitions.All
            .Where(definition => IsApplicableToRoles(definition, access.Roles))
            .SelectMany(definition => definition.SupportedChannels.Select(channel =>
            {
                var current = configured.FirstOrDefault(
                    x => string.Equals(x.NotificationCode, definition.Code, StringComparison.OrdinalIgnoreCase)
                         && x.Channel == channel);

                var isEnabled = definition.IsMandatory || (current?.IsEnabled ?? definition.DefaultEnabled);

                return new NotificationPreferenceDto(
                    definition.Code,
                    definition.Kind,
                    definition.DisplayName,
                    definition.Category,
                    channel,
                    isEnabled,
                    definition.DefaultEnabled,
                    definition.UserCanDisable,
                    definition.IsMandatory)
                {
                    Description = definition.Description
                };
            }))
            .ToArray();
    }

    public async Task UpdateNotificationPreferencesAsync(
        UpdateNotificationPreferencesRequestDto request,
        CancellationToken ct)
    {
        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var access = await GetAccessAsync(innerCt);

            foreach (var item in request.Preferences)
            {
                var definition = definitions.Get(item.Code);
                if (!IsApplicableToRoles(definition, access.Roles))
                    throw new NgbPermissionDeniedException(
                        new NgbPermissionKey(NgbResourceKinds.System, "notification_preferences", NgbPermissionActions.Manage));

                if (!definition.SupportedChannels.Contains(item.Channel))
                    throw new NgbArgumentInvalidException(nameof(item.Channel), "Unsupported notification channel.");

                if (!item.IsEnabled)
                {
                    if (definition.IsMandatory)
                        throw new NgbArgumentInvalidException(nameof(item.IsEnabled), "This notification cannot be disabled.");

                    if (!definition.UserCanDisable)
                        throw new NgbArgumentInvalidException(nameof(item.IsEnabled), "This notification cannot be disabled.");
                }

                await preferences.UpsertAsync(
                    new NotificationPreferenceRecord(
                        access.UserId,
                        definition.Code,
                        item.Channel,
                        definition.IsMandatory || item.IsEnabled,
                        timeProvider.GetUtcNowDateTime(),
                        Version: 1),
                    innerCt);
            }
        }, ct);

        await NotifyChangedAsync(ct);
    }

    private async Task MutateAsync(
        Func<WorkCenterAccess, DateTime, CancellationToken, Task> mutation,
        CancellationToken ct)
    {
        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var access = await GetAccessAsync(innerCt);
            await mutation(access, timeProvider.GetUtcNowDateTime(), innerCt);
        }, ct);

        await NotifyChangedAsync(ct);
    }

    private Task NotifyChangedAsync(CancellationToken ct)
        => realtime.NotifyChangedAsync(timeProvider.GetUtcNow().UtcTicks, ct);

    private static bool IsApplicableToRoles(
        WorkCenterPreferenceDefinition definition,
        IReadOnlyList<PlatformRole> roles)
        => definition.ApplicableRoleCodes is not { Count: > 0 }
           || roles.Any(role => definition.ApplicableRoleCodes.Contains(role.Code));

    private async Task<WorkCenterAccess> GetAccessAsync(CancellationToken ct)
    {
        var snapshot = await snapshots.GetCurrentAsync(ct);
        if (snapshot is not { UserId: { } userId, IsAuthenticated: true, IsActive: true })
            throw new NgbPermissionDeniedException(
                new NgbPermissionKey(NgbResourceKinds.System, "work_center", NgbPermissionActions.View));

        var roles = await userRoles.GetRolesForUserAsync(userId, ct);
        var activeRoles = roles.Where(static x => x.IsActive).ToArray();

        return new WorkCenterAccess(
            userId,
            activeRoles.Select(static x => x.RoleId).ToArray(),
            activeRoles,
            snapshot);
    }

    private static WorkCenterItemDto ToDto(WorkCenterItemRecord row, DateTime now)
    {
        IReadOnlyDictionary<string, string?> parameters = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(row.NavigationParametersJson))
        {
            parameters = JsonSerializer.Deserialize<Dictionary<string, string?>>(row.NavigationParametersJson)
                ?? new Dictionary<string, string?>();
        }

        return new WorkCenterItemDto(
            row.Id,
            row.Kind,
            row.Code,
            row.Title,
            row.Description,
            new WorkCenterSourceDto(
                row.SourceResourceKind,
                row.SourceResourceCode,
                row.SourceEntityId,
                row.SourceTitleSnapshot,
                row.SourceSubtitleSnapshot),
            row.Priority,
            row.Severity,
            row.TaskStatus,
            row.SortAtUtc,
            row.DueAtUtc,
            row.DueAtUtc < now && row.TaskStatus is WorkCenterTaskStatus.Open or WorkCenterTaskStatus.InProgress,
            row.IsRead,
            row.SnoozedUntilUtc,
            row.Kind == WorkCenterItemKind.Task
                ? new WorkCenterAssignmentDto(
                    row.AssignedUserId,
                    row.AssignedRoleId,
                    row.ClaimedByUserId,
                    row.AssignedRoleId is not null)
                : null,
            row.PrimaryActionCode,
            row.NavigationTargetCode is null
                ? null
                : new DocumentActionTargetDto(row.NavigationTargetCode, parameters),
            row.Version);
    }

    private static string EncodeCursor(WorkCenterCursor cursor)
    {
        var raw = $"{cursor.SortAtUtc.Ticks}:{cursor.Id:D}";

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static WorkCenterCursor? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        try
        {
            var base64 = cursor.Trim().Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var parts = raw.Split(':', 2);

            if (parts.Length != 2
                || !long.TryParse(parts[0], out var ticks)
                || !Guid.TryParse(parts[1], out var id))
            {
                throw new FormatException();
            }

            return new WorkCenterCursor(new DateTime(ticks, DateTimeKind.Utc), id);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        {
            throw new NgbArgumentInvalidException(nameof(cursor), "Work Center cursor is invalid.");
        }
    }

    private static WorkCenterVisibility GetVisibility(PermissionSnapshot snapshot)
    {
        if (snapshot.IsBootstrapAdmin)
            return new WorkCenterVisibility(true, [], []);

        var permissions = snapshot.Permissions
            .Where(static x => string.Equals(
                x.ActionCode,
                NgbPermissionActions.View,
                StringComparison.OrdinalIgnoreCase))
            .Select(static x => (x.ResourceKind, x.ResourceCode))
            .Distinct()
            .ToArray();

        return new WorkCenterVisibility(
            false,
            permissions.Select(static x => x.ResourceKind).ToArray(),
            permissions.Select(static x => x.ResourceCode).ToArray());
    }

    private sealed record WorkCenterAccess(
        Guid UserId,
        IReadOnlyList<Guid> RoleIds,
        IReadOnlyList<PlatformRole> Roles,
        PermissionSnapshot Snapshot);

    private sealed record WorkCenterVisibility(
        bool AllowAll,
        IReadOnlyList<string> ResourceKinds,
        IReadOnlyList<string> ResourceCodes);
}
