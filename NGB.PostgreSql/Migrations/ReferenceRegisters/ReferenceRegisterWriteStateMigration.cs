using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.ReferenceRegisters;

/// <summary>
/// Ephemeral idempotency state for Reference Register writes when record_mode = SubordinateToRecorder.
///
/// One row per (register_id, document_id, operation) written in the same transaction as register records.
/// Allows safe retries on timeouts / crashes.
///
/// IMPORTANT:
/// - This table is technical mutable state, not immutable history.
/// - Immutable attempt history lives in reference_register_write_log_history.
///
/// Operation is stored as SMALLINT:
/// 1 = Post, 2 = Unpost, 3 = Repost
/// </summary>
public sealed class ReferenceRegisterWriteStateMigration : IDdlObject
{
    public string Name => "reference_register_write_state";

    public string Generate() => """
                                DO $$
                                BEGIN
                                    IF to_regclass('public.reference_register_write_state') IS NULL
                                       AND to_regclass('public.reference_register_write_log') IS NOT NULL THEN
                                        ALTER TABLE reference_register_write_log RENAME TO reference_register_write_state;
                                    END IF;
                                END
                                $$;

                                CREATE TABLE IF NOT EXISTS reference_register_write_state (
                                    register_id      uuid         NOT NULL,
                                    document_id      uuid         NOT NULL,
                                    operation        smallint     NOT NULL,

                                    attempt_id       uuid         NULL,
                                    started_at_utc   timestamptz  NOT NULL,
                                    completed_at_utc timestamptz  NULL,

                                    CONSTRAINT pk_reference_register_write_state PRIMARY KEY (register_id, document_id, operation),

                                    CONSTRAINT fk_refreg_write_log_register
                                        FOREIGN KEY (register_id)
                                        REFERENCES reference_registers(register_id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT fk_refreg_write_log_document
                                        FOREIGN KEY (document_id)
                                        REFERENCES documents(id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT ck_refreg_write_log_operation
                                        CHECK (operation IN (1, 2, 3)),

                                    CONSTRAINT ck_refreg_write_log_completed_after_started
                                        CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)
                                );

                                ALTER TABLE reference_register_write_state
                                    ADD COLUMN IF NOT EXISTS attempt_id uuid;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'reference_register_write_state'::regclass
                                          AND contype = 'p'
                                    ) THEN
                                        ALTER TABLE reference_register_write_state
                                            ADD CONSTRAINT pk_reference_register_write_state PRIMARY KEY (register_id, document_id, operation);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'reference_register_write_state'::regclass
                                          AND conname = 'fk_refreg_write_log_register'
                                    ) THEN
                                        ALTER TABLE reference_register_write_state
                                            ADD CONSTRAINT fk_refreg_write_log_register
                                                FOREIGN KEY (register_id)
                                                REFERENCES reference_registers(register_id)
                                                ON DELETE CASCADE;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'reference_register_write_state'::regclass
                                          AND conname = 'fk_refreg_write_log_document'
                                    ) THEN
                                        ALTER TABLE reference_register_write_state
                                            ADD CONSTRAINT fk_refreg_write_log_document
                                                FOREIGN KEY (document_id)
                                                REFERENCES documents(id)
                                                ON DELETE CASCADE;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'reference_register_write_state'::regclass
                                          AND conname = 'ck_refreg_write_log_operation'
                                    ) THEN
                                        ALTER TABLE reference_register_write_state
                                            ADD CONSTRAINT ck_refreg_write_log_operation
                                                CHECK (operation IN (1, 2, 3));
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'reference_register_write_state'::regclass
                                          AND conname = 'ck_refreg_write_log_completed_after_started'
                                    ) THEN
                                        ALTER TABLE reference_register_write_state
                                            ADD CONSTRAINT ck_refreg_write_log_completed_after_started
                                                CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc);
                                    END IF;
                                END
                                $$;
                                """;
}
