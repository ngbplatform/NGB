using System.Diagnostics;
using System.Text;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.WorkCenter;
using NGB.Core.Security;
using NGB.Core.WorkCenter;
using NGB.Definitions.WorkCenter;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.WorkCenter;
using NGB.Runtime.Security;
using NGB.Runtime.Observability;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NotificationChannel = NGB.Core.WorkCenter.NotificationChannel;
using NotificationSeverity = NGB.Core.WorkCenter.NotificationSeverity;
using WorkCenterItemKind = NGB.Core.WorkCenter.WorkCenterItemKind;
using WorkCenterPreferenceKind = NGB.Core.WorkCenter.WorkCenterPreferenceKind;
using WorkCenterPriority = NGB.Core.WorkCenter.WorkCenterPriority;
using WorkCenterTaskStatus = NGB.Core.WorkCenter.WorkCenterTaskStatus;

namespace NGB.Runtime.WorkCenter;

internal sealed class WorkCenterPreferenceRecipientResolver(
    INotificationPreferenceRepository preferences,
    IPlatformUserRepository users,
    IPlatformRoleRepository roles,
    IPlatformUserRoleRepository userRoles,
    WorkCenterPreferenceDefinitionRegistry definitions)
{
    private readonly Dictionary<Guid, NGB.Core.AuditLog.PlatformUser?> _users = [];
    private readonly HashSet<Guid> _loadedUsers = [];
    private readonly Dictionary<Guid, IReadOnlyList<PlatformRole>> _rolesByUser = [];
    private readonly HashSet<Guid> _loadedRoleUsers = [];
    private readonly Dictionary<(Guid UserId, string Code, NotificationChannel Channel), NotificationPreferenceRecord> _preferences = [];
    private readonly HashSet<Guid> _loadedPreferenceUsers = [];
    private readonly Dictionary<string, PlatformRole?> _rolesByCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, IReadOnlyList<Guid>> _membersByRole = [];

    public void Reset()
    {
        _users.Clear();
        _loadedUsers.Clear();
        _rolesByUser.Clear();
        _loadedRoleUsers.Clear();
        _preferences.Clear();
        _loadedPreferenceUsers.Clear();
        _rolesByCode.Clear();
        _membersByRole.Clear();
    }

    public async Task<(Guid RoleId, IReadOnlyList<Guid> Recipients)> ResolveRoleAssignmentAsync(
        string preferenceCode,
        WorkCenterPreferenceKind expectedKind,
        string roleCode,
        CancellationToken ct)
    {
        if (!_rolesByCode.TryGetValue(roleCode, out var role))
        {
            role = await roles.GetByCodeAsync(roleCode, ct);
            _rolesByCode[roleCode] = role;
        }

        if (role is null || !role.IsActive)
            throw new NgbConfigurationViolationException($"Work Center assignment role '{roleCode}' is not registered or active.");

        if (!_membersByRole.TryGetValue(role.RoleId, out var candidates))
        {
            candidates = await userRoles.GetUserIdsForRoleAsync(role.RoleId, ct);
            _membersByRole[role.RoleId] = candidates;
        }

        var recipients = await ResolveAsync(preferenceCode, expectedKind, candidates, ct);
        return (role.RoleId, recipients);
    }

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

        var missingUsers = recipients.Where(x => !_loadedUsers.Contains(x)).ToArray();
        if (missingUsers.Length > 0)
        {
            var loaded = await users.GetByIdsAsync(missingUsers, ct);

            foreach (var userId in missingUsers)
            {
                _loadedUsers.Add(userId);
                _users[userId] = loaded.GetValueOrDefault(userId);
            }
        }

        recipients = recipients
            .Where(userId => _users[userId] is { IsActive: true })
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
            var missingRoleUsers = recipients.Where(x => !_loadedRoleUsers.Contains(x)).ToArray();
            if (missingRoleUsers.Length > 0)
            {
                var loadedRoles = await userRoles.GetRolesForUsersAsync(missingRoleUsers, ct);
                foreach (var userId in missingRoleUsers)
                {
                    _loadedRoleUsers.Add(userId);
                    _rolesByUser[userId] = loadedRoles.GetValueOrDefault(userId) ?? [];
                }
            }

            recipients = recipients
                .Where(userId => _rolesByUser[userId].Any(role =>
                    role.IsActive
                    && definition.ApplicableRoleCodes.Contains(role.Code)))
                .ToArray();

            if (recipients.Length == 0)
                return [];
        }

        var missingPreferenceUsers = recipients.Where(x => !_loadedPreferenceUsers.Contains(x)).ToArray();
        if (missingPreferenceUsers.Length > 0)
        {
            var configured = await preferences.GetForUsersAsync(missingPreferenceUsers, ct);
            foreach (var userId in missingPreferenceUsers)
            {
                _loadedPreferenceUsers.Add(userId);
            }

            foreach (var item in configured)
            {
                _preferences[(item.UserId, item.NotificationCode.ToUpperInvariant(), item.Channel)] = item;
            }
        }

        return recipients
            .Where(userId =>
            {
                _preferences.TryGetValue(
                    (userId, definition.Code.ToUpperInvariant(), NotificationChannel.InApp),
                    out var configured);

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
    WorkCenterPreferenceRecipientResolver recipientResolver,
    TimeProvider timeProvider)
    : IWorkCenterTaskService
{
    public Task<WorkCenterMutationResult> CreateAsync(CreateWorkCenterTaskRequest request, CancellationToken ct)
        => InTransactionAsync(async innerCt =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var hasUserAssignment = request.AssignedUserId.HasValue;
            var hasRoleAssignment = !string.IsNullOrWhiteSpace(request.AssignedRoleCode);
            if (hasUserAssignment == hasRoleAssignment)
                throw new NgbArgumentInvalidException("assignment", "Exactly one assigned user or role code is required.");

            Guid? roleId = null;

            if (hasRoleAssignment)
            {
                var roleResolution = await recipientResolver.ResolveRoleAssignmentAsync(
                    request.TaskCode,
                    WorkCenterPreferenceKind.Task,
                    request.AssignedRoleCode!,
                    innerCt);
                roleId = roleResolution.RoleId;

                if (roleResolution.Recipients.Count == 0)
                    return WorkCenterMutationResult.Empty;

                return await CreateTaskAsync(request, roleId, roleResolution.Recipients, innerCt);
            }

            var recipients = await recipientResolver.ResolveAsync(
                request.TaskCode,
                WorkCenterPreferenceKind.Task,
                [request.AssignedUserId!.Value],
                innerCt);

            if (recipients.Count == 0)
                return WorkCenterMutationResult.Empty;

            return await CreateTaskAsync(request, roleId, recipients, innerCt);
        }, ct);

    private async Task<WorkCenterMutationResult> CreateTaskAsync(
        CreateWorkCenterTaskRequest request,
        Guid? roleId,
        IReadOnlyList<Guid> recipients,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNowDateTime();
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
                    now,
                    CompletedAtUtc: null,
                    CancelledAtUtc: null,
                    request.DeduplicationKey,
                    Version: 1,
                    request.CorrelationId,
                    request.CausationId),
                request.PrimaryActionCode?.Value,
                request.Target is null
                    ? null
                    : new WorkCenterNavigationTargetRecord(request.Target.Code, request.Target.Parameters),
                recipients,
                ct);

        return new WorkCenterMutationResult(result.TaskId, result.BecameActive ? recipients : []);
    }

    public Task<IReadOnlyList<Guid>> CompleteByDeduplicationKeyAsync(
        string taskCode,
        string deduplicationKey,
        CancellationToken ct)
        => InTransactionAsync(async innerCt =>
        {
            var result = await tasks.CompleteByDeduplicationKeyAsync(
                taskCode,
                deduplicationKey,
                timeProvider.GetUtcNowDateTime(),
                innerCt);

            return result.RecipientUserIds;
        }, ct);

    public Task<IReadOnlyList<Guid>> CompleteByDeduplicationKeysAsync(
        string taskCode,
        IReadOnlyCollection<string> deduplicationKeys,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(deduplicationKeys);

        if (deduplicationKeys.Count == 0)
            return Task.FromResult<IReadOnlyList<Guid>>([]);

        return InTransactionAsync(async innerCt =>
        {
            var result = await tasks.CompleteByDeduplicationKeysAsync(
                taskCode,
                deduplicationKeys,
                timeProvider.GetUtcNowDateTime(),
                innerCt);

            return result.RecipientUserIds;
        }, ct);
    }

    public Task<IReadOnlyList<Guid>> CancelByDeduplicationKeyAsync(
        string taskCode,
        string deduplicationKey,
        CancellationToken ct)
        => InTransactionAsync(async innerCt =>
        {
            var result = await tasks.CancelByDeduplicationKeyAsync(
                taskCode,
                deduplicationKey,
                timeProvider.GetUtcNowDateTime(),
                innerCt);

            return result.RecipientUserIds;
        }, ct);

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
    public Task<WorkCenterMutationResult> CreateAsync(CreateNotificationRequest request, CancellationToken ct)
        => uow.ExecuteInUowTransactionAsync(!uow.HasActiveTransaction, async innerCt =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var definition = definitions.Get(request.DefinitionCode);

            if (definition.Kind != WorkCenterPreferenceKind.Notification)
            {
                throw new NgbConfigurationViolationException(
                    $"Work Center preference definition '{definition.Code}' is registered as " +
                    $"'{definition.Kind}' and cannot create a notification.");
            }

            IReadOnlyList<Guid> enabledRecipients;
            if (!string.IsNullOrWhiteSpace(request.RecipientRoleCode))
            {
                if (request.RecipientUserIds.Count > 0)
                    throw new NgbArgumentInvalidException("recipients", "Specify either recipient users or a recipient role, not both.");

                var resolution = await recipientResolver.ResolveRoleAssignmentAsync(
                    request.DefinitionCode,
                    WorkCenterPreferenceKind.Notification,
                    request.RecipientRoleCode,
                    innerCt);

                enabledRecipients = resolution.Recipients;
            }
            else
            {
                enabledRecipients = await recipientResolver.ResolveAsync(
                    request.DefinitionCode,
                    WorkCenterPreferenceKind.Notification,
                    request.RecipientUserIds,
                    innerCt);
            }

            if (enabledRecipients.Count == 0)
                return WorkCenterMutationResult.Empty;

            var now = timeProvider.GetUtcNowDateTime();
            var expires = request.ExpiresAtUtc
                ?? (definition.Retention is { } retention ? now.Add(retention) : null);

            var createResult = await notifications.CreateAsync(
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
                    request.CausationId),
                enabledRecipients,
                innerCt);

            NgbFeatureTelemetry.WorkCenterNotificationsCreated.Add(
                1,
                new KeyValuePair<string, object?>("notification.code", definition.Code),
                new KeyValuePair<string, object?>("notification.severity", request.Severity ?? definition.DefaultSeverity));

            return new WorkCenterMutationResult(createResult.NotificationId, createResult.CreatedRecipientUserIds);
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
                    ToRepositoryView(query.Tab),
                    query.Vertical,
                    query.Priority is { } priority ? (WorkCenterPriority)priority : null,
                    query.Severity is { } severity ? (NotificationSeverity)severity : null,
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

    private static WorkCenterQueryView ToRepositoryView(WorkCenterTab tab)
    {
        if (tab == WorkCenterTab.Tasks)
            return WorkCenterQueryView.Tasks;

        if (tab == WorkCenterTab.Notifications)
            return WorkCenterQueryView.Notifications;

        if (tab == WorkCenterTab.Completed)
            return WorkCenterQueryView.Completed;

        return WorkCenterQueryView.Attention;
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
        var configuredByCodeAndChannel = configured.ToDictionary(
            static x => (x.NotificationCode.ToUpperInvariant(), x.Channel));

        return definitions.All
            .Where(definition => IsApplicableToRoles(definition, access.Roles))
            .SelectMany(definition => definition.SupportedChannels.Select(channel =>
            {
                configuredByCodeAndChannel.TryGetValue(
                    (definition.Code.ToUpperInvariant(), channel),
                    out var current);

                var isEnabled = definition.IsMandatory || (current?.IsEnabled ?? definition.DefaultEnabled);

                return new NotificationPreferenceDto(
                    definition.Code,
                    (NGB.Contracts.WorkCenter.WorkCenterPreferenceKind)definition.Kind,
                    definition.DisplayName,
                    definition.Category,
                    (NGB.Contracts.WorkCenter.NotificationChannel)channel,
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
        var userId = await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var access = await GetAccessAsync(innerCt);
            var now = timeProvider.GetUtcNowDateTime();
            var updates = new Dictionary<(string Code, NotificationChannel Channel), NotificationPreferenceRecord>();

            foreach (var item in request.Preferences)
            {
                var definition = definitions.Get(item.Code);
                if (!IsApplicableToRoles(definition, access.Roles))
                    throw new NgbPermissionDeniedException(new NgbPermissionKey(NgbResourceKinds.System, "notification_preferences", NgbPermissionActions.Manage));

                var channel = (NotificationChannel)item.Channel;
                if (!definition.SupportedChannels.Contains(channel))
                    throw new NgbArgumentInvalidException(nameof(item.Channel), "Unsupported notification channel.");

                if (!item.IsEnabled)
                {
                    if (definition.IsMandatory)
                        throw new NgbArgumentInvalidException(nameof(item.IsEnabled), "This notification cannot be disabled.");

                    if (!definition.UserCanDisable)
                        throw new NgbArgumentInvalidException(nameof(item.IsEnabled), "This notification cannot be disabled.");
                }

                updates[(definition.Code.ToUpperInvariant(), channel)] = new NotificationPreferenceRecord(
                        access.UserId,
                        definition.Code,
                        channel,
                        definition.IsMandatory || item.IsEnabled,
                        now,
                        Version: 1);
            }

            await preferences.UpsertManyAsync(updates.Values.ToArray(), innerCt);

            return access.UserId;
        }, ct);

        await NotifyChangedAsync([userId], ct);
    }

    private async Task MutateAsync(
        Func<WorkCenterAccess, DateTime, CancellationToken, Task> mutation,
        CancellationToken ct)
    {
        var userId = await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var access = await GetAccessAsync(innerCt);
            await mutation(access, timeProvider.GetUtcNowDateTime(), innerCt);

            return access.UserId;
        }, ct);

        await NotifyChangedAsync([userId], ct);
    }

    private Task NotifyChangedAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
        => realtime.NotifyUsersChangedAsync(timeProvider.GetUtcNow().UtcTicks, userIds, ct);

    private static bool IsApplicableToRoles(WorkCenterPreferenceDefinition definition, IReadOnlyList<PlatformRole> roles)
        => definition.ApplicableRoleCodes is not { Count: > 0 }
           || roles.Any(role => definition.ApplicableRoleCodes.Contains(role.Code));

    private async Task<WorkCenterAccess> GetAccessAsync(CancellationToken ct)
    {
        var snapshot = await snapshots.GetCurrentAsync(ct);
        if (snapshot is not { UserId: { } userId, IsAuthenticated: true, IsActive: true })
            throw new NgbPermissionDeniedException(new NgbPermissionKey(NgbResourceKinds.System, "work_center", NgbPermissionActions.View));

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
        return new WorkCenterItemDto(
            row.Id,
            (NGB.Contracts.WorkCenter.WorkCenterItemKind)row.Kind,
            row.Code,
            row.Title,
            row.Description,
            new WorkCenterSourceDto(
                row.SourceResourceKind,
                row.SourceResourceCode,
                row.SourceEntityId,
                row.SourceTitleSnapshot,
                row.SourceSubtitleSnapshot),
            row.Priority is { } priority ? (NGB.Contracts.WorkCenter.WorkCenterPriority)priority : null,
            row.Severity is { } severity ? (NGB.Contracts.WorkCenter.NotificationSeverity)severity : null,
            row.TaskStatus is { } status ? (NGB.Contracts.WorkCenter.WorkCenterTaskStatus)status : null,
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
            row.Target is null
                ? null
                : new DocumentActionTargetDto(row.Target.Code, row.Target.Parameters),
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

            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out var id))
                throw new FormatException();

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
