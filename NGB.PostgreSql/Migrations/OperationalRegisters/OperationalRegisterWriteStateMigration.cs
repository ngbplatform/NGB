using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.OperationalRegisters;

/// <summary>
/// Ephemeral idempotency state for operational register writes.
///
/// One row per (register_id, document_id, operation) written in the same transaction as register movements.
/// Allows safe retries on timeouts / crashes.
///
/// IMPORTANT:
/// - This table is technical mutable state, not immutable history.
/// - Immutable attempt history lives in operational_register_write_log_history.
///
/// Operation is stored as SMALLINT for compactness and to prevent invalid values.
/// </summary>
public sealed class OperationalRegisterWriteStateMigration : IDdlObject
{
    public string Name => "operational_register_write_state";

    public string Generate() => """
                                DO $$
                                BEGIN
                                    IF to_regclass('public.operational_register_write_state') IS NULL
                                       AND to_regclass('public.operational_register_write_log') IS NOT NULL THEN
                                        ALTER TABLE operational_register_write_log RENAME TO operational_register_write_state;
                                    END IF;
                                END
                                $$;

                                CREATE TABLE IF NOT EXISTS operational_register_write_state (
                                    register_id      uuid         NOT NULL,
                                    document_id      uuid         NOT NULL,
                                    operation        smallint     NOT NULL,

                                    attempt_id       uuid         NULL,
                                    started_at_utc   timestamptz  NOT NULL,
                                    completed_at_utc timestamptz  NULL,

                                    CONSTRAINT pk_operational_register_write_state PRIMARY KEY (register_id, document_id, operation),

                                    CONSTRAINT fk_opreg_write_log_register
                                        FOREIGN KEY (register_id)
                                        REFERENCES operational_registers(register_id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT fk_opreg_write_log_document
                                        FOREIGN KEY (document_id)
                                        REFERENCES documents(id)
                                        ON DELETE CASCADE,

                                    -- Allowed operations (mirrors PostingOperation used in accounting):
                                    -- 1 = Post, 2 = Unpost, 3 = Repost
                                    CONSTRAINT ck_opreg_write_log_operation
                                        CHECK (operation IN (1, 2, 3)),

                                    CONSTRAINT ck_opreg_write_log_completed_after_started
                                        CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)
                                );

                                ALTER TABLE operational_register_write_state
                                    ADD COLUMN IF NOT EXISTS attempt_id uuid;

                                ALTER TABLE operational_register_write_state
                                    ADD COLUMN IF NOT EXISTS register_id uuid,
                                    ADD COLUMN IF NOT EXISTS document_id uuid,
                                    ADD COLUMN IF NOT EXISTS operation smallint,
                                    ADD COLUMN IF NOT EXISTS started_at_utc timestamptz,
                                    ADD COLUMN IF NOT EXISTS completed_at_utc timestamptz;


                                DO $$
                                BEGIN
                                    -- Drift repair: ensure required columns exist before constraints.
                                    -- (Some test cases drop columns; keeping this inside the DO block avoids multi-statement parse/plan ordering issues.)
                                    EXECUTE $ddl$
                                        ALTER TABLE operational_register_write_state
                                            ADD COLUMN IF NOT EXISTS register_id uuid,
                                            ADD COLUMN IF NOT EXISTS document_id uuid,
                                            ADD COLUMN IF NOT EXISTS operation smallint,
                                            ADD COLUMN IF NOT EXISTS attempt_id uuid,
                                            ADD COLUMN IF NOT EXISTS started_at_utc timestamptz,
                                            ADD COLUMN IF NOT EXISTS completed_at_utc timestamptz
                                    $ddl$;

                                    -- Drift-repair: CREATE TABLE IF NOT EXISTS will not re-create dropped constraints.

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_write_state'::regclass
                                          AND contype = 'p'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_write_state
                                                ADD CONSTRAINT pk_operational_register_write_state PRIMARY KEY (register_id, document_id, operation)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_write_state'::regclass
                                          AND conname = 'fk_opreg_write_log_register'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_write_state
                                                ADD CONSTRAINT fk_opreg_write_log_register
                                                    FOREIGN KEY (register_id)
                                                    REFERENCES operational_registers(register_id)
                                                    ON DELETE CASCADE
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_write_state'::regclass
                                          AND conname = 'fk_opreg_write_log_document'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_write_state
                                                ADD CONSTRAINT fk_opreg_write_log_document
                                                    FOREIGN KEY (document_id)
                                                    REFERENCES documents(id)
                                                    ON DELETE CASCADE
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_write_state'::regclass
                                          AND conname = 'ck_opreg_write_log_operation'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_write_state
                                                ADD CONSTRAINT ck_opreg_write_log_operation
                                                    CHECK (operation IN (1, 2, 3))
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_write_state'::regclass
                                          AND conname = 'ck_opreg_write_log_completed_after_started'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_write_state
                                                ADD CONSTRAINT ck_opreg_write_log_completed_after_started
                                                    CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)
                                        $ddl$;
                                    END IF;
                                END
                                $$;
                                """;
}
