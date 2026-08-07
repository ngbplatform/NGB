using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

public sealed class PlatformDocumentActionsWorkCenterMigration : IDdlObject
{
    public string Name => "platform_document_actions_work_center";

    public string Generate() => """
        ALTER TABLE documents
            ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 1;

        ALTER TABLE documents
            DROP CONSTRAINT IF EXISTS ck_documents_version_positive;
        ALTER TABLE documents
            ADD CONSTRAINT ck_documents_version_positive CHECK (version > 0);

        CREATE TABLE IF NOT EXISTS platform_document_action_executions (
            execution_id uuid PRIMARY KEY,
            idempotency_key text NOT NULL,
            request_fingerprint text NOT NULL,
            document_id uuid NOT NULL REFERENCES documents(id) ON DELETE RESTRICT,
            document_type text NOT NULL,
            action_code varchar(128) NOT NULL,
            started_at_utc timestamptz NOT NULL,
            completed_at_utc timestamptz NULL,
            result_json jsonb NULL,
            CONSTRAINT ux_platform_document_action_executions_key UNIQUE (idempotency_key),
            CONSTRAINT ck_platform_document_action_execution_key CHECK (length(trim(idempotency_key)) BETWEEN 1 AND 200),
            CONSTRAINT ck_platform_document_action_execution_fingerprint CHECK (length(request_fingerprint) = 64),
            CONSTRAINT ck_platform_document_action_execution_code CHECK (
                action_code = lower(trim(action_code))
                AND action_code ~ '^[a-z0-9._:-]+$'
            ),
            CONSTRAINT ck_platform_document_action_execution_completion CHECK (
                (completed_at_utc IS NULL AND result_json IS NULL)
                OR
                (completed_at_utc IS NOT NULL AND result_json IS NOT NULL AND completed_at_utc >= started_at_utc)
            )
        );

        CREATE INDEX IF NOT EXISTS ix_platform_document_action_executions_document
            ON platform_document_action_executions(document_id, started_at_utc DESC);

        CREATE TABLE IF NOT EXISTS platform_outbox_events (
            event_id uuid PRIMARY KEY,
            event_type varchar(160) NOT NULL,
            schema_version integer NOT NULL,
            occurred_at_utc timestamptz NOT NULL,
            source varchar(100) NOT NULL,
            subject varchar(300) NOT NULL,
            actor_user_id uuid NULL REFERENCES platform_users(user_id) ON DELETE SET NULL,
            correlation_id uuid NOT NULL,
            causation_id uuid NULL,
            payload_json jsonb NOT NULL,
            created_at_utc timestamptz NOT NULL,
            CONSTRAINT ck_platform_outbox_event_type CHECK (length(trim(event_type)) BETWEEN 1 AND 160),
            CONSTRAINT ck_platform_outbox_schema_version CHECK (schema_version > 0),
            CONSTRAINT ck_platform_outbox_source CHECK (length(trim(source)) BETWEEN 1 AND 100),
            CONSTRAINT ck_platform_outbox_subject CHECK (length(trim(subject)) BETWEEN 1 AND 300)
        );

        CREATE TABLE IF NOT EXISTS platform_outbox_consumer_state (
            event_id uuid NOT NULL REFERENCES platform_outbox_events(event_id) ON DELETE RESTRICT,
            consumer_code varchar(128) NOT NULL,
            status smallint NOT NULL,
            attempt_count integer NOT NULL DEFAULT 0,
            next_attempt_at_utc timestamptz NOT NULL,
            locked_at_utc timestamptz NULL,
            completed_at_utc timestamptz NULL,
            last_error varchar(2000) NULL,
            PRIMARY KEY (event_id, consumer_code),
            CONSTRAINT ck_platform_outbox_consumer_code CHECK (length(trim(consumer_code)) BETWEEN 1 AND 128),
            CONSTRAINT ck_platform_outbox_consumer_status CHECK (status IN (1, 2, 3, 4, 5)),
            CONSTRAINT ck_platform_outbox_attempt_count CHECK (attempt_count >= 0),
            CONSTRAINT ck_platform_outbox_completion CHECK (
                (status = 3 AND completed_at_utc IS NOT NULL)
                OR (status <> 3)
            )
        );

        CREATE INDEX IF NOT EXISTS ix_platform_outbox_consumer_pending
            ON platform_outbox_consumer_state(consumer_code, next_attempt_at_utc, event_id)
            WHERE status IN (1, 4);

        CREATE TABLE IF NOT EXISTS platform_outbox_consumer_history (
            history_id uuid PRIMARY KEY,
            event_id uuid NOT NULL REFERENCES platform_outbox_events(event_id) ON DELETE RESTRICT,
            consumer_code varchar(128) NOT NULL,
            attempt_number integer NOT NULL,
            started_at_utc timestamptz NOT NULL,
            completed_at_utc timestamptz NOT NULL,
            outcome smallint NOT NULL,
            error_metadata varchar(2000) NULL,
            CONSTRAINT ux_platform_outbox_consumer_history_attempt UNIQUE (event_id, consumer_code, attempt_number),
            CONSTRAINT ck_platform_outbox_history_attempt CHECK (attempt_number > 0),
            CONSTRAINT ck_platform_outbox_history_outcome CHECK (outcome IN (1, 2, 3)),
            CONSTRAINT ck_platform_outbox_history_time CHECK (completed_at_utc >= started_at_utc)
        );

        CREATE TABLE IF NOT EXISTS platform_notifications (
            id uuid PRIMARY KEY,
            definition_code varchar(128) NOT NULL,
            source_resource_kind varchar(64) NOT NULL,
            source_resource_code varchar(160) NOT NULL,
            source_entity_id uuid NOT NULL,
            source_title_snapshot varchar(300) NOT NULL,
            source_subtitle_snapshot varchar(500) NULL,
            title varchar(300) NOT NULL,
            body varchar(2000) NULL,
            severity smallint NOT NULL,
            created_at_utc timestamptz NOT NULL,
            expires_at_utc timestamptz NULL,
            deduplication_key varchar(300) NOT NULL,
            correlation_id uuid NULL,
            causation_id uuid NULL,
            metadata_json jsonb NULL,
            CONSTRAINT ux_platform_notifications_dedup UNIQUE (definition_code, deduplication_key),
            CONSTRAINT ck_platform_notifications_definition CHECK (
                definition_code = lower(trim(definition_code))
                AND definition_code ~ '^[a-z0-9._:-]+$'
            ),
            CONSTRAINT ck_platform_notifications_severity CHECK (severity IN (1, 2, 3, 4)),
            CONSTRAINT ck_platform_notifications_title CHECK (length(trim(title)) BETWEEN 1 AND 300),
            CONSTRAINT ck_platform_notifications_expiry CHECK (expires_at_utc IS NULL OR expires_at_utc > created_at_utc)
        );

        CREATE TABLE IF NOT EXISTS platform_notification_deliveries (
            notification_id uuid NOT NULL REFERENCES platform_notifications(id) ON DELETE CASCADE,
            user_id uuid NOT NULL REFERENCES platform_users(user_id) ON DELETE CASCADE,
            created_at_utc timestamptz NOT NULL,
            read_at_utc timestamptz NULL,
            dismissed_at_utc timestamptz NULL,
            PRIMARY KEY (notification_id, user_id),
            CONSTRAINT ck_platform_notification_delivery_read CHECK (read_at_utc IS NULL OR read_at_utc >= created_at_utc),
            CONSTRAINT ck_platform_notification_delivery_dismissed CHECK (dismissed_at_utc IS NULL OR dismissed_at_utc >= created_at_utc)
        );

        CREATE INDEX IF NOT EXISTS ix_platform_notification_deliveries_attention
            ON platform_notification_deliveries(user_id, created_at_utc DESC, notification_id DESC)
            WHERE read_at_utc IS NULL AND dismissed_at_utc IS NULL;
        CREATE INDEX IF NOT EXISTS ix_platform_notification_deliveries_active
            ON platform_notification_deliveries(user_id, created_at_utc DESC, notification_id DESC)
            WHERE dismissed_at_utc IS NULL;

        CREATE TABLE IF NOT EXISTS platform_tasks (
            id uuid PRIMARY KEY,
            task_code varchar(128) NOT NULL,
            preference_code varchar(128) NULL,
            source_resource_kind varchar(64) NOT NULL,
            source_resource_code varchar(160) NOT NULL,
            source_entity_id uuid NOT NULL,
            source_title_snapshot varchar(300) NOT NULL,
            source_subtitle_snapshot varchar(500) NULL,
            title varchar(300) NOT NULL,
            description varchar(2000) NULL,
            priority smallint NOT NULL,
            status smallint NOT NULL,
            assigned_user_id uuid NULL REFERENCES platform_users(user_id) ON DELETE RESTRICT,
            assigned_role_id uuid NULL REFERENCES platform_roles(role_id) ON DELETE RESTRICT,
            claimed_by_user_id uuid NULL REFERENCES platform_users(user_id) ON DELETE RESTRICT,
            due_at_utc timestamptz NULL,
            primary_action_code varchar(128) NULL,
            navigation_target_code varchar(160) NULL,
            navigation_parameters_json jsonb NOT NULL DEFAULT '{}'::jsonb,
            created_at_utc timestamptz NOT NULL,
            updated_at_utc timestamptz NOT NULL,
            completed_at_utc timestamptz NULL,
            cancelled_at_utc timestamptz NULL,
            deduplication_key varchar(300) NOT NULL,
            version bigint NOT NULL DEFAULT 1,
            correlation_id uuid NULL,
            causation_id uuid NULL,
            metadata_json jsonb NULL,
            CONSTRAINT ux_platform_tasks_dedup UNIQUE (task_code, deduplication_key),
            CONSTRAINT ck_platform_tasks_code CHECK (
                task_code = lower(trim(task_code))
                AND task_code ~ '^[a-z0-9._:-]+$'
            ),
            CONSTRAINT ck_platform_tasks_preference_code CHECK (
                preference_code IS NULL
                OR (
                    preference_code = lower(trim(preference_code))
                    AND preference_code ~ '^[a-z0-9._:-]+$'
                )
            ),
            CONSTRAINT ck_platform_tasks_assignment CHECK (
                (assigned_user_id IS NOT NULL AND assigned_role_id IS NULL)
                OR
                (assigned_user_id IS NULL AND assigned_role_id IS NOT NULL)
            ),
            CONSTRAINT ck_platform_tasks_claim CHECK (
                claimed_by_user_id IS NULL OR assigned_role_id IS NOT NULL
            ),
            CONSTRAINT ck_platform_tasks_priority CHECK (priority IN (1, 2, 3, 4)),
            CONSTRAINT ck_platform_tasks_status CHECK (status IN (1, 2, 3, 4)),
            CONSTRAINT ck_platform_tasks_version CHECK (version > 0),
            CONSTRAINT ck_platform_tasks_title CHECK (length(trim(title)) BETWEEN 1 AND 300),
            CONSTRAINT ck_platform_tasks_timestamps CHECK (
                updated_at_utc >= created_at_utc
                AND (completed_at_utc IS NULL OR completed_at_utc >= created_at_utc)
                AND (cancelled_at_utc IS NULL OR cancelled_at_utc >= created_at_utc)
                AND (status <> 3 OR completed_at_utc IS NOT NULL)
                AND (status <> 4 OR cancelled_at_utc IS NOT NULL)
            )
        );

        CREATE INDEX IF NOT EXISTS ix_platform_tasks_user_open
            ON platform_tasks(assigned_user_id, due_at_utc, created_at_utc DESC, id DESC)
            WHERE status IN (1, 2);
        CREATE INDEX IF NOT EXISTS ix_platform_tasks_role_open
            ON platform_tasks(assigned_role_id, due_at_utc, created_at_utc DESC, id DESC)
            WHERE status IN (1, 2) AND claimed_by_user_id IS NULL;
        CREATE INDEX IF NOT EXISTS ix_platform_tasks_claimed_open
            ON platform_tasks(claimed_by_user_id, created_at_utc DESC, id DESC)
            WHERE status IN (1, 2) AND claimed_by_user_id IS NOT NULL;

        CREATE TABLE IF NOT EXISTS platform_task_recipients (
            task_id uuid NOT NULL REFERENCES platform_tasks(id) ON DELETE CASCADE,
            user_id uuid NOT NULL REFERENCES platform_users(user_id) ON DELETE CASCADE,
            created_at_utc timestamptz NOT NULL,
            PRIMARY KEY (task_id, user_id)
        );

        CREATE INDEX IF NOT EXISTS ix_platform_task_recipients_user
            ON platform_task_recipients(user_id, task_id);
        CREATE INDEX IF NOT EXISTS ix_platform_task_recipients_user_feed
            ON platform_task_recipients(user_id, created_at_utc DESC, task_id DESC);

        CREATE TABLE IF NOT EXISTS platform_task_user_states (
            task_id uuid NOT NULL REFERENCES platform_tasks(id) ON DELETE CASCADE,
            user_id uuid NOT NULL REFERENCES platform_users(user_id) ON DELETE CASCADE,
            read_at_utc timestamptz NULL,
            snoozed_until_utc timestamptz NULL,
            PRIMARY KEY (task_id, user_id)
        );

        CREATE TABLE IF NOT EXISTS platform_user_notification_preferences (
            user_id uuid NOT NULL REFERENCES platform_users(user_id) ON DELETE CASCADE,
            notification_code varchar(128) NOT NULL,
            channel smallint NOT NULL,
            is_enabled boolean NOT NULL,
            updated_at_utc timestamptz NOT NULL,
            version bigint NOT NULL DEFAULT 1,
            PRIMARY KEY (user_id, notification_code, channel),
            CONSTRAINT ck_platform_notification_preferences_code CHECK (
                notification_code = lower(trim(notification_code))
                AND notification_code ~ '^[a-z0-9._:-]+$'
            ),
            CONSTRAINT ck_platform_notification_preferences_channel CHECK (channel = 1),
            CONSTRAINT ck_platform_notification_preferences_version CHECK (version > 0)
        );

        INSERT INTO platform_task_recipients (task_id, user_id, created_at_utc)
        SELECT task.id, task.assigned_user_id, task.created_at_utc
        FROM platform_tasks task
        JOIN platform_users platform_user
          ON platform_user.user_id = task.assigned_user_id
         AND platform_user.is_active
        LEFT JOIN platform_user_notification_preferences preference
          ON preference.user_id = task.assigned_user_id
         AND preference.notification_code = task.preference_code
         AND preference.channel = 1
        WHERE task.assigned_user_id IS NOT NULL
          AND (task.preference_code IS NULL OR COALESCE(preference.is_enabled, true))
        ON CONFLICT (task_id, user_id) DO NOTHING;

        INSERT INTO platform_task_recipients (task_id, user_id, created_at_utc)
        SELECT task.id, user_role.user_id, task.created_at_utc
        FROM platform_tasks task
        JOIN platform_user_roles user_role
          ON user_role.role_id = task.assigned_role_id
        JOIN platform_users platform_user
          ON platform_user.user_id = user_role.user_id
         AND platform_user.is_active
        LEFT JOIN platform_user_notification_preferences preference
          ON preference.user_id = user_role.user_id
         AND preference.notification_code = task.preference_code
         AND preference.channel = 1
        WHERE task.assigned_role_id IS NOT NULL
          AND (task.preference_code IS NULL OR COALESCE(preference.is_enabled, true))
        ON CONFLICT (task_id, user_id) DO NOTHING;
        """;
}
