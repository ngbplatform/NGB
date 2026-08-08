using System.Text.Json;
using Dapper;
using NGB.Core.WorkCenter;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.WorkCenter;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.WorkCenter;

public sealed class PostgresWorkCenterRepository(IUnitOfWork uow)
    : IWorkCenterTaskRepository,
      INotificationRepository,
      INotificationPreferenceRepository,
      IWorkCenterReadRepository,
      IWorkCenterMaintenanceRepository
{
    public async Task<WorkCenterPruneResult> PruneAsync(
        WorkCenterRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken ct)
    {
        if (batchSize is < 1 or > 10_000)
            throw new NgbArgumentInvalidException(nameof(batchSize), "Batch size must be between 1 and 10000.");

        cutoffs.DocumentActionExecutionsBeforeUtc.EnsureUtc(nameof(cutoffs.DocumentActionExecutionsBeforeUtc));
        cutoffs.TerminalTasksBeforeUtc.EnsureUtc(nameof(cutoffs.TerminalTasksBeforeUtc));
        cutoffs.NotificationDeliveriesBeforeUtc.EnsureUtc(nameof(cutoffs.NotificationDeliveriesBeforeUtc));
        cutoffs.OutboxBeforeUtc.EnsureUtc(nameof(cutoffs.OutboxBeforeUtc));
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
            WITH action_candidates AS MATERIALIZED (
                SELECT execution_id
                FROM platform_document_action_executions
                WHERE completed_at_utc < @DocumentActionExecutionsBeforeUtc
                ORDER BY completed_at_utc, execution_id
                LIMIT @BatchSize
            ),
            deleted_actions AS (
                DELETE FROM platform_document_action_executions execution
                USING action_candidates candidate
                WHERE execution.execution_id = candidate.execution_id
                RETURNING execution.execution_id
            ),
            task_candidates AS MATERIALIZED (
                SELECT id
                FROM platform_tasks
                WHERE status IN (@CompletedStatus, @CancelledStatus)
                  AND updated_at_utc < @TerminalTasksBeforeUtc
                ORDER BY updated_at_utc, id
                LIMIT @BatchSize
            ),
            deleted_tasks AS (
                DELETE FROM platform_tasks task
                USING task_candidates candidate
                WHERE task.id = candidate.id
                RETURNING task.id
            ),
            delivery_candidates AS MATERIALIZED (
                SELECT delivery.notification_id, delivery.user_id
                FROM platform_notification_deliveries delivery
                JOIN platform_notifications notification ON notification.id = delivery.notification_id
                WHERE COALESCE(delivery.dismissed_at_utc, notification.expires_at_utc) < @NotificationDeliveriesBeforeUtc
                ORDER BY COALESCE(delivery.dismissed_at_utc, notification.expires_at_utc), delivery.notification_id, delivery.user_id
                LIMIT @BatchSize
            ),
            deleted_deliveries AS (
                DELETE FROM platform_notification_deliveries delivery
                USING delivery_candidates candidate
                WHERE delivery.notification_id = candidate.notification_id
                  AND delivery.user_id = candidate.user_id
                RETURNING delivery.notification_id
            ),
            notification_candidates AS MATERIALIZED (
                SELECT notification.id
                FROM platform_notifications notification
                WHERE notification.created_at_utc < @NotificationDeliveriesBeforeUtc
                  AND NOT EXISTS (
                    SELECT 1 FROM platform_notification_deliveries delivery
                    WHERE delivery.notification_id = notification.id
                      AND NOT EXISTS (
                        SELECT 1
                        FROM delivery_candidates candidate
                        WHERE candidate.notification_id = delivery.notification_id
                          AND candidate.user_id = delivery.user_id
                      )
                  )
                ORDER BY notification.created_at_utc, notification.id
                LIMIT @BatchSize
            ),
            deleted_notifications AS (
                DELETE FROM platform_notifications notification
                USING notification_candidates candidate
                WHERE notification.id = candidate.id
                  AND (SELECT count(*) FROM deleted_deliveries) >= 0
                RETURNING notification.id
            ),
            outbox_candidates AS MATERIALIZED (
                SELECT event.event_id
                FROM platform_outbox_events event
                WHERE event.created_at_utc < @OutboxBeforeUtc
                  AND NOT EXISTS (
                    SELECT 1
                    FROM platform_outbox_consumer_state state
                    WHERE state.event_id = event.event_id
                      AND state.status IN (@PendingStatus, @ProcessingStatus, @FailedStatus)
                  )
                ORDER BY event.created_at_utc, event.event_id
                LIMIT @BatchSize
            ),
            deleted_outbox_history AS (
                DELETE FROM platform_outbox_consumer_history history
                USING outbox_candidates candidate
                WHERE history.event_id = candidate.event_id
                RETURNING history.event_id
            ),
            deleted_outbox_state AS (
                DELETE FROM platform_outbox_consumer_state state
                USING outbox_candidates candidate
                WHERE state.event_id = candidate.event_id
                RETURNING state.event_id
            ),
            deleted_outbox AS (
                DELETE FROM platform_outbox_events event
                USING outbox_candidates candidate
                WHERE event.event_id = candidate.event_id
                  AND (SELECT count(*) FROM deleted_outbox_history) >= 0
                  AND (SELECT count(*) FROM deleted_outbox_state) >= 0
                RETURNING event.event_id
            )
            SELECT
                (SELECT count(*) FROM deleted_actions)::int AS "DocumentActionExecutions",
                (SELECT count(*) FROM deleted_tasks)::int AS "Tasks",
                (SELECT count(*) FROM deleted_deliveries)::int AS "NotificationDeliveries",
                (SELECT count(*) FROM deleted_notifications)::int AS "Notifications",
                (SELECT count(*) FROM deleted_outbox)::int AS "OutboxEvents";
            """;

        return await uow.Connection.QuerySingleAsync<WorkCenterPruneResult>(
            new CommandDefinition(
                sql,
                new
                {
                    cutoffs.DocumentActionExecutionsBeforeUtc,
                    cutoffs.TerminalTasksBeforeUtc,
                    cutoffs.NotificationDeliveriesBeforeUtc,
                    cutoffs.OutboxBeforeUtc,
                    BatchSize = batchSize,
                    CompletedStatus = (short)WorkCenterTaskStatus.Completed,
                    CancelledStatus = (short)WorkCenterTaskStatus.Cancelled,
                    PendingStatus = (short)NGB.Persistence.Outbox.OutboxConsumerStatus.Pending,
                    ProcessingStatus = (short)NGB.Persistence.Outbox.OutboxConsumerStatus.Processing,
                    FailedStatus = (short)NGB.Persistence.Outbox.OutboxConsumerStatus.Failed
                },
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<WorkCenterTaskCreateResult> CreateAsync(
        WorkCenterTask task,
        string? primaryActionCode,
        WorkCenterNavigationTargetRecord? target,
        IReadOnlyList<Guid> recipientUserIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(recipientUserIds);

        var recipients = recipientUserIds
            .Where(static x => x != Guid.Empty)
            .Distinct()
            .ToArray();

        if (recipients.Length == 0)
            throw new NgbArgumentInvalidException(nameof(recipientUserIds), "At least one task recipient is required.");

        ValidateAssignment(task.AssignedUserId, task.AssignedRoleId);
        task.CreatedAtUtc.EnsureUtc(nameof(task.CreatedAtUtc));

        await uow.EnsureOpenForTransactionAsync(ct);

        const string insertSql = """
            INSERT INTO platform_tasks (
                id, task_code, preference_code,
                source_resource_kind, source_resource_code, source_entity_id,
                source_title_snapshot, source_subtitle_snapshot,
                title, description, priority, status,
                assigned_user_id, assigned_role_id, claimed_by_user_id,
                due_at_utc, primary_action_code,
                navigation_target_code, navigation_parameters_json,
                created_at_utc, updated_at_utc, completed_at_utc, cancelled_at_utc,
                deduplication_key, version, correlation_id, causation_id
            )
            VALUES (
                @Id, @TaskCode, @PreferenceCode,
                @SourceResourceKind, @SourceResourceCode, @SourceEntityId,
                @SourceTitleSnapshot, @SourceSubtitleSnapshot,
                @Title, @Description, @Priority, @Status,
                @AssignedUserId, @AssignedRoleId, @ClaimedByUserId,
                @DueAtUtc, @PrimaryActionCode,
                @NavigationTargetCode, CAST(@NavigationParametersJson AS jsonb),
                @CreatedAtUtc, @CreatedAtUtc, @CompletedAtUtc, @CancelledAtUtc,
                @DeduplicationKey, @Version, @CorrelationId, @CausationId
            )
            ON CONFLICT (task_code, deduplication_key)
            DO NOTHING
            RETURNING id AS TaskId, true AS BecameActive, version;
            """;

        var parameters = new
        {
            task.Id,
            task.TaskCode,
            task.PreferenceCode,
            SourceResourceKind = task.Source.ResourceKind,
            SourceResourceCode = task.Source.ResourceCode,
            SourceEntityId = task.Source.EntityId,
            SourceTitleSnapshot = task.Source.TitleSnapshot,
            SourceSubtitleSnapshot = task.Source.SubtitleSnapshot,
            task.Title,
            task.Description,
            Priority = (short)task.Priority,
            Status = (short)task.Status,
            task.AssignedUserId,
            task.AssignedRoleId,
            task.ClaimedByUserId,
            task.DueAtUtc,
            PrimaryActionCode = primaryActionCode,
            NavigationTargetCode = target?.Code,
            NavigationParametersJson = JsonSerializer.Serialize(target?.Parameters ?? new Dictionary<string, string?>()),
            task.CreatedAtUtc,
            task.CompletedAtUtc,
            task.CancelledAtUtc,
            task.DeduplicationKey,
            task.Version,
            task.CorrelationId,
            task.CausationId,
            CompletedStatus = (short)WorkCenterTaskStatus.Completed,
            CancelledStatus = (short)WorkCenterTaskStatus.Cancelled,
            InProgressStatus = (short)WorkCenterTaskStatus.InProgress
        };

        var inserted = await uow.Connection.QuerySingleOrDefaultAsync<WorkCenterTaskCreateResult>(
            new CommandDefinition(
                insertSql,
                parameters,
                uow.Transaction,
                cancellationToken: ct));

        if (inserted is not null)
        {
            await ReplaceTaskRecipientsAsync(
                inserted.TaskId,
                recipients,
                task.CreatedAtUtc,
                ct);

            return inserted;
        }

        const string lockSql = """
            SELECT
                id AS TaskId,
                status AS Status,
                claimed_by_user_id AS ClaimedByUserId,
                version AS Version
            FROM platform_tasks
            WHERE task_code = @TaskCode
              AND deduplication_key = @DeduplicationKey
            FOR UPDATE;
            """;
        var existing = await uow.Connection.QuerySingleAsync<ExistingTaskState>(
            new CommandDefinition(
                lockSql,
                parameters,
                uow.Transaction,
                cancellationToken: ct));

        var existingStatus = (WorkCenterTaskStatus)existing.Status;
        if (existingStatus is not (WorkCenterTaskStatus.Completed or WorkCenterTaskStatus.Cancelled))
            return new WorkCenterTaskCreateResult(existing.TaskId, BecameActive: false, existing.Version);

        const string reopenSql = """
            UPDATE platform_tasks
            SET status = CASE
                    WHEN claimed_by_user_id IS NULL THEN @Status
                    ELSE @InProgressStatus
                END,
                completed_at_utc = NULL,
                cancelled_at_utc = NULL,
                updated_at_utc = @CreatedAtUtc,
                version = version + 1
            WHERE id = @TaskId
            RETURNING id AS TaskId, true AS BecameActive, version;
            """;
        var reopened = await uow.Connection.QuerySingleAsync<WorkCenterTaskCreateResult>(
            new CommandDefinition(
                reopenSql,
                new
                {
                    existing.TaskId,
                    Status = (short)task.Status,
                    InProgressStatus = (short)WorkCenterTaskStatus.InProgress,
                    task.CreatedAtUtc
                },
                uow.Transaction,
                cancellationToken: ct));

        await ReplaceTaskRecipientsAsync(
            reopened.TaskId,
            recipients,
            task.CreatedAtUtc,
            ct);

        return reopened;
    }

    public async Task<WorkCenterTaskMutationResult> CompleteByDeduplicationKeyAsync(
        string taskCode,
        string deduplicationKey,
        DateTime completedAtUtc,
        CancellationToken ct)
    {
        completedAtUtc.EnsureUtc(nameof(completedAtUtc));
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
            WITH updated AS (
            UPDATE platform_tasks
            SET status = @Status,
                completed_at_utc = @CompletedAtUtc,
                cancelled_at_utc = NULL,
                updated_at_utc = @CompletedAtUtc,
                version = version + 1
            WHERE task_code = @TaskCode
              AND deduplication_key = @DeduplicationKey
              AND status IN (@OpenStatus, @InProgressStatus)
            RETURNING id
            )
            SELECT DISTINCT recipient.user_id
            FROM updated
            JOIN platform_task_recipients recipient ON recipient.task_id = updated.id;
            """;

        var recipients = (await uow.Connection.QueryAsync<Guid>(
            new CommandDefinition(
                sql,
                new
                {
                    TaskCode = taskCode,
                    DeduplicationKey = deduplicationKey,
                    Status = (short)WorkCenterTaskStatus.Completed,
                    OpenStatus = (short)WorkCenterTaskStatus.Open,
                    InProgressStatus = (short)WorkCenterTaskStatus.InProgress,
                    CompletedAtUtc = completedAtUtc
                },
                uow.Transaction,
                cancellationToken: ct))).ToArray();

        return new WorkCenterTaskMutationResult(recipients.Length > 0, recipients);
    }

    public async Task<WorkCenterTaskMutationResult> CancelByDeduplicationKeyAsync(
        string taskCode,
        string deduplicationKey,
        DateTime cancelledAtUtc,
        CancellationToken ct)
    {
        cancelledAtUtc.EnsureUtc(nameof(cancelledAtUtc));
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
            WITH updated AS (
            UPDATE platform_tasks
            SET status = @Status,
                cancelled_at_utc = @CancelledAtUtc,
                completed_at_utc = NULL,
                updated_at_utc = @CancelledAtUtc,
                version = version + 1
            WHERE task_code = @TaskCode
              AND deduplication_key = @DeduplicationKey
              AND status IN (@OpenStatus, @InProgressStatus)
            RETURNING id
            )
            SELECT DISTINCT recipient.user_id
            FROM updated
            JOIN platform_task_recipients recipient ON recipient.task_id = updated.id;
            """;

        var recipients = (await uow.Connection.QueryAsync<Guid>(
            new CommandDefinition(
                sql,
                new
                {
                    TaskCode = taskCode,
                    DeduplicationKey = deduplicationKey,
                    Status = (short)WorkCenterTaskStatus.Cancelled,
                    OpenStatus = (short)WorkCenterTaskStatus.Open,
                    InProgressStatus = (short)WorkCenterTaskStatus.InProgress,
                    CancelledAtUtc = cancelledAtUtc
                },
                uow.Transaction,
                cancellationToken: ct))).ToArray();

        return new WorkCenterTaskMutationResult(recipients.Length > 0, recipients);
    }

    public Task MarkReadAsync(
        Guid taskId,
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        bool allowAllSources,
        IReadOnlyList<string> allowedResourceKinds,
        IReadOnlyList<string> allowedResourceCodes,
        DateTime readAtUtc,
        CancellationToken ct)
        => UpsertTaskUserStateAsync(
            taskId,
            userId,
            roleIds,
            allowAllSources,
            allowedResourceKinds,
            allowedResourceCodes,
            readAtUtc,
            snoozedUntilUtc: null,
            updateRead: true,
            ct);

    public Task SnoozeAsync(
        Guid taskId,
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        bool allowAllSources,
        IReadOnlyList<string> allowedResourceKinds,
        IReadOnlyList<string> allowedResourceCodes,
        DateTime snoozedUntilUtc,
        CancellationToken ct)
        => UpsertTaskUserStateAsync(
            taskId,
            userId,
            roleIds,
            allowAllSources,
            allowedResourceKinds,
            allowedResourceCodes,
            readAtUtc: null,
            snoozedUntilUtc,
            updateRead: false,
            ct);

    public async Task<bool> ClaimAsync(
        Guid taskId,
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        bool allowAllSources,
        IReadOnlyList<string> allowedResourceKinds,
        IReadOnlyList<string> allowedResourceCodes,
        long expectedVersion,
        DateTime claimedAtUtc,
        CancellationToken ct)
    {
        claimedAtUtc.EnsureUtc(nameof(claimedAtUtc));
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
            UPDATE platform_tasks
            SET claimed_by_user_id = @UserId,
                status = @InProgressStatus,
                updated_at_utc = @ClaimedAtUtc,
                version = version + 1
            WHERE id = @TaskId
              AND assigned_role_id = ANY(@RoleIds)
              AND EXISTS (
                SELECT 1
                FROM platform_task_recipients recipient
                WHERE recipient.task_id = platform_tasks.id
                  AND recipient.user_id = @UserId
              )
              AND claimed_by_user_id IS NULL
              AND status = @OpenStatus
              AND version = @ExpectedVersion
              AND (
                @AllowAllSources
                OR EXISTS (
                  SELECT 1
                  FROM unnest(@AllowedResourceKinds::text[], @AllowedResourceCodes::text[])
                    AS allowed(resource_kind, resource_code)
                  WHERE allowed.resource_kind = platform_tasks.source_resource_kind
                    AND allowed.resource_code = platform_tasks.source_resource_code
                )
              );
            """;

        var rows = await uow.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    TaskId = taskId,
                    UserId = userId,
                    RoleIds = roleIds.ToArray(),
                    AllowAllSources = allowAllSources,
                    AllowedResourceKinds = allowedResourceKinds.ToArray(),
                    AllowedResourceCodes = allowedResourceCodes.ToArray(),
                    ExpectedVersion = expectedVersion,
                    OpenStatus = (short)WorkCenterTaskStatus.Open,
                    InProgressStatus = (short)WorkCenterTaskStatus.InProgress,
                    ClaimedAtUtc = claimedAtUtc
                },
                uow.Transaction,
                cancellationToken: ct));

        return rows == 1;
    }

    public async Task<WorkCenterNotificationCreateResult> CreateAsync(
        WorkCenterNotification notification,
        IReadOnlyList<Guid> recipientUserIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);
        notification.CreatedAtUtc.EnsureUtc(nameof(notification.CreatedAtUtc));
        await uow.EnsureOpenForTransactionAsync(ct);

        const string notificationSql = """
            INSERT INTO platform_notifications (
                id, definition_code,
                source_resource_kind, source_resource_code, source_entity_id,
                source_title_snapshot, source_subtitle_snapshot,
                title, body, severity, created_at_utc, expires_at_utc,
                deduplication_key, correlation_id, causation_id
            )
            VALUES (
                @Id, @DefinitionCode,
                @SourceResourceKind, @SourceResourceCode, @SourceEntityId,
                @SourceTitleSnapshot, @SourceSubtitleSnapshot,
                @Title, @Body, @Severity, @CreatedAtUtc, @ExpiresAtUtc,
                @DeduplicationKey, @CorrelationId, @CausationId
            )
            ON CONFLICT (definition_code, deduplication_key)
            DO UPDATE SET definition_code = platform_notifications.definition_code
            RETURNING id;
            """;

        var notificationId = await uow.Connection.QuerySingleAsync<Guid>(
            new CommandDefinition(
                notificationSql,
                new
                {
                    notification.Id,
                    notification.DefinitionCode,
                    SourceResourceKind = notification.Source.ResourceKind,
                    SourceResourceCode = notification.Source.ResourceCode,
                    SourceEntityId = notification.Source.EntityId,
                    SourceTitleSnapshot = notification.Source.TitleSnapshot,
                    SourceSubtitleSnapshot = notification.Source.SubtitleSnapshot,
                    notification.Title,
                    notification.Body,
                    Severity = (short)notification.Severity,
                    notification.CreatedAtUtc,
                    notification.ExpiresAtUtc,
                    notification.DeduplicationKey,
                    notification.CorrelationId,
                    notification.CausationId
                },
                uow.Transaction,
                cancellationToken: ct));

        const string deliverySql = """
            INSERT INTO platform_notification_deliveries (
                notification_id, user_id, created_at_utc
            )
            SELECT @NotificationId, recipients.user_id, @CreatedAtUtc
            FROM unnest(@RecipientUserIds::uuid[]) AS recipients(user_id)
            ON CONFLICT (notification_id, user_id) DO NOTHING
            RETURNING user_id;
            """;

        var createdRecipients = (await uow.Connection.QueryAsync<Guid>(
            new CommandDefinition(
                deliverySql,
                new
                {
                    NotificationId = notificationId,
                    RecipientUserIds = recipientUserIds.Distinct().ToArray(),
                    notification.CreatedAtUtc
                },
                uow.Transaction,
                cancellationToken: ct)))
            .ToArray();

        return new WorkCenterNotificationCreateResult(notificationId, createdRecipients);
    }

    public Task MarkReadAsync(
        Guid notificationId,
        Guid userId,
        bool allowAllSources,
        IReadOnlyList<string> allowedResourceKinds,
        IReadOnlyList<string> allowedResourceCodes,
        DateTime readAtUtc,
        CancellationToken ct)
        => UpdateNotificationDeliveryAsync(
            notificationId,
            userId,
            allowAllSources,
            allowedResourceKinds,
            allowedResourceCodes,
            readAtUtc,
            dismiss: false,
            ct);

    public Task DismissAsync(
        Guid notificationId,
        Guid userId,
        bool allowAllSources,
        IReadOnlyList<string> allowedResourceKinds,
        IReadOnlyList<string> allowedResourceCodes,
        DateTime dismissedAtUtc,
        CancellationToken ct)
        => UpdateNotificationDeliveryAsync(
            notificationId,
            userId,
            allowAllSources,
            allowedResourceKinds,
            allowedResourceCodes,
            dismissedAtUtc,
            dismiss: true,
            ct);

    public async Task<IReadOnlyList<NotificationPreferenceRecord>> GetForUserAsync(
        Guid userId,
        CancellationToken ct)
        => await GetForUsersAsync([userId], ct);

    public async Task<IReadOnlyList<NotificationPreferenceRecord>> GetForUsersAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken ct)
    {
        if (userIds.Count == 0)
            return [];

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
            SELECT user_id AS "UserId",
                   notification_code AS "NotificationCode",
                   channel AS "Channel",
                   is_enabled AS "IsEnabled",
                   updated_at_utc AS "UpdatedAtUtc",
                   version AS "Version"
            FROM platform_user_notification_preferences
            WHERE user_id = ANY(@UserIds);
            """;

        var rows = await uow.Connection.QueryAsync<PreferenceRow>(
            new CommandDefinition(
                sql,
                new { UserIds = userIds.Distinct().ToArray() },
                uow.Transaction,
                cancellationToken: ct));

        return rows.Select(static x => new NotificationPreferenceRecord(
            x.UserId,
            x.NotificationCode,
            (NotificationChannel)x.Channel,
            x.IsEnabled,
            x.UpdatedAtUtc,
            x.Version)).ToArray();
    }

    public async Task UpsertAsync(NotificationPreferenceRecord preference, CancellationToken ct)
        => await UpsertManyAsync([preference], ct);

    public async Task UpsertManyAsync(IReadOnlyList<NotificationPreferenceRecord> preferences, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (preferences.Count == 0)
            return;

        foreach (var preference in preferences)
        {
            preference.UpdatedAtUtc.EnsureUtc(nameof(preference.UpdatedAtUtc));
        }

        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
            INSERT INTO platform_user_notification_preferences (
                user_id, notification_code, channel, is_enabled, updated_at_utc, version
            )
            SELECT input.user_id,
                   input.notification_code,
                   input.channel,
                   input.is_enabled,
                   input.updated_at_utc,
                   1
            FROM UNNEST(
                @UserIds::uuid[],
                @NotificationCodes::text[],
                @Channels::smallint[],
                @Enabled::boolean[],
                @UpdatedAtUtc::timestamptz[]
            ) AS input(user_id, notification_code, channel, is_enabled, updated_at_utc)
            ON CONFLICT (user_id, notification_code, channel)
            DO UPDATE SET is_enabled = EXCLUDED.is_enabled,
                          updated_at_utc = EXCLUDED.updated_at_utc,
                          version = platform_user_notification_preferences.version + 1;
            """;

        await uow.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    UserIds = preferences.Select(static x => x.UserId).ToArray(),
                    NotificationCodes = preferences.Select(static x => x.NotificationCode).ToArray(),
                    Channels = preferences.Select(static x => (short)x.Channel).ToArray(),
                    Enabled = preferences.Select(static x => x.IsEnabled).ToArray(),
                    UpdatedAtUtc = preferences.Select(static x => x.UpdatedAtUtc).ToArray()
                },
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<WorkCenterSummaryRecord> GetSummaryAsync(
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        bool allowAllSources,
        IReadOnlyList<string> allowedResourceKinds,
        IReadOnlyList<string> allowedResourceCodes,
        string? vertical,
        DateTime nowUtc,
        CancellationToken ct)
    {
        nowUtc.EnsureUtc(nameof(nowUtc));
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
            WITH open_tasks AS (
                SELECT t.id, t.due_at_utc, t.updated_at_utc, s.snoozed_until_utc
                FROM platform_tasks t
                JOIN platform_task_recipients recipient
                  ON recipient.task_id = t.id AND recipient.user_id = @UserId
                LEFT JOIN platform_task_user_states s
                  ON s.task_id = t.id AND s.user_id = @UserId
                WHERE t.status IN (@OpenStatus, @InProgressStatus)
                  AND (
                    t.assigned_user_id = @UserId
                    OR t.claimed_by_user_id = @UserId
                    OR (
                        t.assigned_role_id = ANY(@RoleIds)
                        AND (t.claimed_by_user_id IS NULL OR t.claimed_by_user_id = @UserId)
                    )
                  )
                  AND (
                    @AllowAllSources
                    OR EXISTS (
                      SELECT 1
                      FROM unnest(@AllowedResourceKinds::text[], @AllowedResourceCodes::text[])
                        AS allowed(resource_kind, resource_code)
                      WHERE allowed.resource_kind = t.source_resource_kind
                        AND allowed.resource_code = t.source_resource_code
                    )
                  )
                  AND (@Vertical::text IS NULL OR t.source_resource_code LIKE @Vertical::text || '.%')
            ),
            attention_tasks AS (
                SELECT id, due_at_utc, updated_at_utc
                FROM open_tasks
                WHERE snoozed_until_utc IS NULL OR snoozed_until_utc <= @NowUtc
            ),
            visible_notifications AS (
                SELECT n.id, d.created_at_utc, d.read_at_utc
                FROM platform_notification_deliveries d
                JOIN platform_notifications n ON n.id = d.notification_id
                WHERE d.user_id = @UserId
                  AND d.dismissed_at_utc IS NULL
                  AND (n.expires_at_utc IS NULL OR n.expires_at_utc > @NowUtc)
                  AND (
                    @AllowAllSources
                    OR EXISTS (
                      SELECT 1
                      FROM unnest(@AllowedResourceKinds::text[], @AllowedResourceCodes::text[])
                        AS allowed(resource_kind, resource_code)
                      WHERE allowed.resource_kind = n.source_resource_kind
                        AND allowed.resource_code = n.source_resource_code
                    )
                  )
                  AND (@Vertical::text IS NULL OR n.source_resource_code LIKE @Vertical::text || '.%')
            ),
            unread_notifications AS (
                SELECT id, created_at_utc
                FROM visible_notifications
                WHERE read_at_utc IS NULL
            )
            SELECT
                (SELECT count(*) FROM attention_tasks)::int
                    + (SELECT count(*) FROM unread_notifications)::int AS "AttentionCount",
                (SELECT count(*) FROM open_tasks)::int AS "OpenTaskCount",
                (SELECT count(*) FROM attention_tasks WHERE due_at_utc < @NowUtc)::int AS "OverdueTaskCount",
                (SELECT count(*) FROM visible_notifications)::int AS "NotificationCount",
                (SELECT count(*) FROM unread_notifications)::int AS "UnreadNotificationCount",
                GREATEST(
                    COALESCE((SELECT floor(extract(epoch FROM max(updated_at_utc)) * 1000)::bigint FROM open_tasks), 0),
                    COALESCE((SELECT floor(extract(epoch FROM max(created_at_utc)) * 1000)::bigint FROM visible_notifications), 0)
                ) AS "Version";
            """;

        return await uow.Connection.QuerySingleAsync<WorkCenterSummaryRecord>(
            new CommandDefinition(
                sql,
                new
                {
                    UserId = userId,
                    RoleIds = roleIds.ToArray(),
                    AllowAllSources = allowAllSources,
                    AllowedResourceKinds = allowedResourceKinds.ToArray(),
                    AllowedResourceCodes = allowedResourceCodes.ToArray(),
                    Vertical = string.IsNullOrWhiteSpace(vertical) ? null : vertical.Trim(),
                    NowUtc = nowUtc,
                    OpenStatus = (short)WorkCenterTaskStatus.Open,
                    InProgressStatus = (short)WorkCenterTaskStatus.InProgress
                },
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<WorkCenterTaskHealthRecord> GetTaskHealthAsync(DateTime nowUtc, CancellationToken ct)
    {
        nowUtc.EnsureUtc(nameof(nowUtc));
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
            SELECT count(*) AS "OpenTaskCount",
                   count(*) FILTER (WHERE due_at_utc < @NowUtc) AS "OverdueTaskCount"
            FROM platform_tasks task
            WHERE task.status IN (@OpenStatus, @InProgressStatus)
              AND EXISTS (
                SELECT 1
                FROM platform_task_recipients recipient
                WHERE recipient.task_id = task.id
              );
            """;

        return await uow.Connection.QuerySingleAsync<WorkCenterTaskHealthRecord>(
            new CommandDefinition(
                sql,
                new
                {
                    OpenStatus = (short)WorkCenterTaskStatus.Open,
                    InProgressStatus = (short)WorkCenterTaskStatus.InProgress,
                    NowUtc = nowUtc
                },
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<WorkCenterItemRecord>> GetItemsAsync(
        WorkCenterQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        await uow.EnsureConnectionOpenAsync(ct);

        const string taskSelect = """
                SELECT
                    t.id,
                    @TaskKind::smallint AS kind,
                    t.task_code AS code,
                    t.title,
                    t.description,
                    t.source_resource_kind,
                    t.source_resource_code,
                    t.source_entity_id,
                    t.source_title_snapshot,
                    t.source_subtitle_snapshot,
                    t.priority,
                    NULL::smallint AS severity,
                    t.status AS task_status,
                    t.created_at_utc AS sort_at_utc,
                    t.due_at_utc,
                    (s.read_at_utc IS NOT NULL) AS is_read,
                    s.snoozed_until_utc,
                    t.assigned_user_id,
                    t.assigned_role_id,
                    t.claimed_by_user_id,
                    t.primary_action_code,
                    t.navigation_target_code,
                    t.navigation_parameters_json::text AS navigation_parameters_json,
                    t.version
                FROM platform_tasks t
                JOIN platform_task_recipients recipient
                  ON recipient.task_id = t.id AND recipient.user_id = @UserId
                LEFT JOIN platform_task_user_states s
                  ON s.task_id = t.id AND s.user_id = @UserId
                WHERE (
                    t.assigned_user_id = @UserId
                    OR t.claimed_by_user_id = @UserId
                    OR (
                        t.assigned_role_id = ANY(@RoleIds)
                        AND (t.claimed_by_user_id IS NULL OR t.claimed_by_user_id = @UserId)
                    )
                )
                  AND (
                    @AllowAllSources
                    OR EXISTS (
                      SELECT 1
                      FROM unnest(@AllowedResourceKinds::text[], @AllowedResourceCodes::text[])
                        AS allowed(resource_kind, resource_code)
                      WHERE allowed.resource_kind = t.source_resource_kind
                        AND allowed.resource_code = t.source_resource_code
                    )
                  )
                  AND (
                    (@IncludeTerminalTasks AND t.status IN (@CompletedStatus, @CancelledStatus))
                    OR (NOT @IncludeTerminalTasks AND t.status IN (@OpenStatus, @InProgressStatus))
                  )
                  AND (NOT @AttentionOnly OR s.snoozed_until_utc IS NULL OR s.snoozed_until_utc <= @NowUtc)
                  AND (@Priority::smallint IS NULL OR t.priority = @Priority::smallint)
                  AND (@Overdue::boolean IS NULL OR (t.due_at_utc < @NowUtc) = @Overdue::boolean)
                  AND (@Unread::boolean IS NULL OR (s.read_at_utc IS NULL) = @Unread::boolean)
                  AND (@Vertical::text IS NULL OR t.source_resource_code LIKE @Vertical::text || '.%')
            """;

        const string notificationSelect = """
                SELECT
                    n.id,
                    @NotificationKind::smallint AS kind,
                    n.definition_code AS code,
                    n.title,
                    n.body AS description,
                    n.source_resource_kind,
                    n.source_resource_code,
                    n.source_entity_id,
                    n.source_title_snapshot,
                    n.source_subtitle_snapshot,
                    NULL::smallint AS priority,
                    n.severity,
                    NULL::smallint AS task_status,
                    d.created_at_utc AS sort_at_utc,
                    NULL::timestamptz AS due_at_utc,
                    (d.read_at_utc IS NOT NULL) AS is_read,
                    NULL::timestamptz AS snoozed_until_utc,
                    NULL::uuid AS assigned_user_id,
                    NULL::uuid AS assigned_role_id,
                    NULL::uuid AS claimed_by_user_id,
                    NULL::varchar AS primary_action_code,
                    NULL::varchar AS navigation_target_code,
                    NULL::text AS navigation_parameters_json,
                    1::bigint AS version
                FROM platform_notification_deliveries d
                JOIN platform_notifications n ON n.id = d.notification_id
                WHERE d.user_id = @UserId
                  AND d.dismissed_at_utc IS NULL
                  AND (
                    @AllowAllSources
                    OR EXISTS (
                      SELECT 1
                      FROM unnest(@AllowedResourceKinds::text[], @AllowedResourceCodes::text[])
                        AS allowed(resource_kind, resource_code)
                      WHERE allowed.resource_kind = n.source_resource_kind
                        AND allowed.resource_code = n.source_resource_code
                    )
                  )
                  AND (n.expires_at_utc IS NULL OR n.expires_at_utc > @NowUtc)
                  AND (NOT @AttentionOnly OR d.read_at_utc IS NULL)
                  AND (@Severity::smallint IS NULL OR n.severity = @Severity::smallint)
                  AND (@Unread::boolean IS NULL OR (d.read_at_utc IS NULL) = @Unread::boolean)
                  AND (@Vertical::text IS NULL OR n.source_resource_code LIKE @Vertical::text || '.%')
            """;

        const string pageSelect = """
            )
            SELECT
                id AS "Id",
                kind AS "Kind",
                code AS "Code",
                title AS "Title",
                description AS "Description",
                source_resource_kind AS "SourceResourceKind",
                source_resource_code AS "SourceResourceCode",
                source_entity_id AS "SourceEntityId",
                source_title_snapshot AS "SourceTitleSnapshot",
                source_subtitle_snapshot AS "SourceSubtitleSnapshot",
                priority AS "Priority",
                severity AS "Severity",
                task_status AS "TaskStatus",
                sort_at_utc AS "SortAtUtc",
                due_at_utc AS "DueAtUtc",
                is_read AS "IsRead",
                snoozed_until_utc AS "SnoozedUntilUtc",
                assigned_user_id AS "AssignedUserId",
                assigned_role_id AS "AssignedRoleId",
                claimed_by_user_id AS "ClaimedByUserId",
                primary_action_code AS "PrimaryActionCode",
                navigation_target_code AS "NavigationTargetCode",
                navigation_parameters_json AS "NavigationParametersJson",
                version AS "Version"
            FROM feed
            WHERE (
                @CursorSortAtUtc::timestamptz IS NULL
                OR (sort_at_utc, id) < (@CursorSortAtUtc::timestamptz, @CursorId::uuid)
              )
            ORDER BY sort_at_utc DESC, id DESC
            LIMIT @Limit;
            """;

        var feedSelect = query.View switch
        {
            WorkCenterQueryView.Tasks or WorkCenterQueryView.Completed => taskSelect,
            WorkCenterQueryView.Notifications => notificationSelect,
            _ => $"{taskSelect}\nUNION ALL\n{notificationSelect}"
        };
        var sql = $"WITH feed AS (\n{feedSelect}\n{pageSelect}";

        var rows = await uow.Connection.QueryAsync<FeedRow>(
            new CommandDefinition(
                sql,
                new
                {
                    query.UserId,
                    RoleIds = query.RoleIds.ToArray(),
                    query.AllowAllSources,
                    AllowedResourceKinds = query.AllowedResourceKinds.ToArray(),
                    AllowedResourceCodes = query.AllowedResourceCodes.ToArray(),
                    query.Vertical,
                    Priority = query.Priority is { } priority ? (short)priority : (short?)null,
                    Severity = query.Severity is { } severity ? (short)severity : (short?)null,
                    query.Overdue,
                    query.Unread,
                    query.NowUtc,
                    CursorSortAtUtc = query.Cursor?.SortAtUtc,
                    CursorId = query.Cursor?.Id,
                    query.Limit,
                    TaskKind = (short)WorkCenterItemKind.Task,
                    NotificationKind = (short)WorkCenterItemKind.Notification,
                    IncludeTerminalTasks = query.View == WorkCenterQueryView.Completed,
                    AttentionOnly = query.View == WorkCenterQueryView.Attention,
                    OpenStatus = (short)WorkCenterTaskStatus.Open,
                    InProgressStatus = (short)WorkCenterTaskStatus.InProgress,
                    CompletedStatus = (short)WorkCenterTaskStatus.Completed,
                    CancelledStatus = (short)WorkCenterTaskStatus.Cancelled
                },
                uow.Transaction,
                cancellationToken: ct));

        return rows.Select(static x => x.ToRecord()).ToArray();
    }

    private async Task UpsertTaskUserStateAsync(
        Guid taskId,
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        bool allowAllSources,
        IReadOnlyList<string> allowedResourceKinds,
        IReadOnlyList<string> allowedResourceCodes,
        DateTime? readAtUtc,
        DateTime? snoozedUntilUtc,
        bool updateRead,
        CancellationToken ct)
    {
        if (readAtUtc is not null)
            readAtUtc.Value.EnsureUtc(nameof(readAtUtc));

        if (snoozedUntilUtc is not null)
            snoozedUntilUtc.Value.EnsureUtc(nameof(snoozedUntilUtc));

        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
            INSERT INTO platform_task_user_states (task_id, user_id, read_at_utc, snoozed_until_utc)
            SELECT t.id, @UserId, @ReadAtUtc, @SnoozedUntilUtc
            FROM platform_tasks t
            WHERE t.id = @TaskId
              AND EXISTS (
                SELECT 1
                FROM platform_task_recipients recipient
                WHERE recipient.task_id = t.id
                  AND recipient.user_id = @UserId
              )
              AND (
                t.assigned_user_id = @UserId
                OR t.claimed_by_user_id = @UserId
                OR (
                    t.assigned_role_id = ANY(@RoleIds)
                    AND (t.claimed_by_user_id IS NULL OR t.claimed_by_user_id = @UserId)
                )
              )
              AND (
                @AllowAllSources
                OR EXISTS (
                  SELECT 1
                  FROM unnest(@AllowedResourceKinds::text[], @AllowedResourceCodes::text[])
                    AS allowed(resource_kind, resource_code)
                  WHERE allowed.resource_kind = t.source_resource_kind
                    AND allowed.resource_code = t.source_resource_code
                )
              )
            ON CONFLICT (task_id, user_id)
            DO UPDATE SET
                read_at_utc = CASE WHEN @UpdateRead THEN EXCLUDED.read_at_utc ELSE platform_task_user_states.read_at_utc END,
                snoozed_until_utc = CASE WHEN @UpdateRead THEN platform_task_user_states.snoozed_until_utc ELSE EXCLUDED.snoozed_until_utc END;
            """;

        var rows = await uow.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    TaskId = taskId,
                    UserId = userId,
                    RoleIds = roleIds.ToArray(),
                    AllowAllSources = allowAllSources,
                    AllowedResourceKinds = allowedResourceKinds.ToArray(),
                    AllowedResourceCodes = allowedResourceCodes.ToArray(),
                    ReadAtUtc = readAtUtc,
                    SnoozedUntilUtc = snoozedUntilUtc,
                    UpdateRead = updateRead
                },
                uow.Transaction,
                cancellationToken: ct));

        if (rows == 0)
            throw new WorkCenterItemNotFoundException(taskId);
    }

    private async Task ReplaceTaskRecipientsAsync(
        Guid taskId,
        IReadOnlyList<Guid> recipientUserIds,
        DateTime createdAtUtc,
        CancellationToken ct)
    {
        const string deleteSql = """
            DELETE FROM platform_task_recipients
            WHERE task_id = @TaskId;
            """;
        await uow.Connection.ExecuteAsync(
            new CommandDefinition(
                deleteSql,
                new { TaskId = taskId },
                uow.Transaction,
                cancellationToken: ct));

        const string insertSql = """
            INSERT INTO platform_task_recipients (task_id, user_id, created_at_utc)
            SELECT @TaskId, recipients.user_id, @CreatedAtUtc
            FROM unnest(@RecipientUserIds::uuid[]) AS recipients(user_id)
            ON CONFLICT (task_id, user_id) DO NOTHING;
            """;

        await uow.Connection.ExecuteAsync(
            new CommandDefinition(
                insertSql,
                new
                {
                    TaskId = taskId,
                    RecipientUserIds = recipientUserIds.Distinct().ToArray(),
                    CreatedAtUtc = createdAtUtc
                },
                uow.Transaction,
                cancellationToken: ct));
    }

    private async Task UpdateNotificationDeliveryAsync(
        Guid notificationId,
        Guid userId,
        bool allowAllSources,
        IReadOnlyList<string> allowedResourceKinds,
        IReadOnlyList<string> allowedResourceCodes,
        DateTime atUtc,
        bool dismiss,
        CancellationToken ct)
    {
        atUtc.EnsureUtc(nameof(atUtc));
        await uow.EnsureOpenForTransactionAsync(ct);

        var sql = dismiss
            ? """
              UPDATE platform_notification_deliveries
              SET dismissed_at_utc = COALESCE(dismissed_at_utc, @AtUtc)
              FROM platform_notifications n
              WHERE platform_notification_deliveries.notification_id = @NotificationId
                AND platform_notification_deliveries.user_id = @UserId
                AND n.id = platform_notification_deliveries.notification_id
                AND (
                  @AllowAllSources
                  OR EXISTS (
                    SELECT 1
                    FROM unnest(@AllowedResourceKinds::text[], @AllowedResourceCodes::text[])
                      AS allowed(resource_kind, resource_code)
                    WHERE allowed.resource_kind = n.source_resource_kind
                      AND allowed.resource_code = n.source_resource_code
                  )
                );
              """
            : """
              UPDATE platform_notification_deliveries
              SET read_at_utc = COALESCE(read_at_utc, @AtUtc)
              FROM platform_notifications n
              WHERE platform_notification_deliveries.notification_id = @NotificationId
                AND platform_notification_deliveries.user_id = @UserId
                AND n.id = platform_notification_deliveries.notification_id
                AND (
                  @AllowAllSources
                  OR EXISTS (
                    SELECT 1
                    FROM unnest(@AllowedResourceKinds::text[], @AllowedResourceCodes::text[])
                      AS allowed(resource_kind, resource_code)
                    WHERE allowed.resource_kind = n.source_resource_kind
                      AND allowed.resource_code = n.source_resource_code
                  )
                );
              """;

        var rows = await uow.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    NotificationId = notificationId,
                    UserId = userId,
                    AllowAllSources = allowAllSources,
                    AllowedResourceKinds = allowedResourceKinds.ToArray(),
                    AllowedResourceCodes = allowedResourceCodes.ToArray(),
                    AtUtc = atUtc
                },
                uow.Transaction,
                cancellationToken: ct));

        if (rows == 0)
            throw new WorkCenterItemNotFoundException(notificationId);
    }

    private static void ValidateAssignment(Guid? userId, Guid? roleId)
    {
        if (userId.HasValue == roleId.HasValue)
            throw new NgbArgumentInvalidException("assignment", "Exactly one task assignment target is required.");
    }

    private sealed class ExistingTaskState
    {
        public Guid TaskId { get; init; }
        public short Status { get; init; }
        public Guid? ClaimedByUserId { get; init; }
        public long Version { get; init; }
    }

    private sealed class PreferenceRow
    {
        public Guid UserId { get; init; }
        public string NotificationCode { get; init; } = null!;
        public short Channel { get; init; }
        public bool IsEnabled { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
        public long Version { get; init; }
    }

    private sealed class FeedRow
    {
        public Guid Id { get; init; }
        public short Kind { get; init; }
        public string Code { get; init; } = null!;
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public string SourceResourceKind { get; init; } = null!;
        public string SourceResourceCode { get; init; } = null!;
        public Guid SourceEntityId { get; init; }
        public string SourceTitleSnapshot { get; init; } = null!;
        public string? SourceSubtitleSnapshot { get; init; }
        public short? Priority { get; init; }
        public short? Severity { get; init; }
        public short? TaskStatus { get; init; }
        public DateTime SortAtUtc { get; init; }
        public DateTime? DueAtUtc { get; init; }
        public bool IsRead { get; init; }
        public DateTime? SnoozedUntilUtc { get; init; }
        public Guid? AssignedUserId { get; init; }
        public Guid? AssignedRoleId { get; init; }
        public Guid? ClaimedByUserId { get; init; }
        public string? PrimaryActionCode { get; init; }
        public string? NavigationTargetCode { get; init; }
        public string? NavigationParametersJson { get; init; }
        public long Version { get; init; }

        public WorkCenterItemRecord ToRecord() => new(
            Id,
            (WorkCenterItemKind)Kind,
            Code,
            Title,
            Description,
            SourceResourceKind,
            SourceResourceCode,
            SourceEntityId,
            SourceTitleSnapshot,
            SourceSubtitleSnapshot,
            Priority is null ? null : (WorkCenterPriority)Priority.Value,
            Severity is null ? null : (NotificationSeverity)Severity.Value,
            TaskStatus is null ? null : (WorkCenterTaskStatus)TaskStatus.Value,
            SortAtUtc,
            DueAtUtc,
            IsRead,
            SnoozedUntilUtc,
            AssignedUserId,
            AssignedRoleId,
            ClaimedByUserId,
            PrimaryActionCode,
            NavigationTargetCode is null
                ? null
                : new WorkCenterNavigationTargetRecord(
                    NavigationTargetCode,
                    JsonSerializer.Deserialize<Dictionary<string, string?>>(NavigationParametersJson!)
                    ?? new Dictionary<string, string?>()),
            Version);
    }
}
