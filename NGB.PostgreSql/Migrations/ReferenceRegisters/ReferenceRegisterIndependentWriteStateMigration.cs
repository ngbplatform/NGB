using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.ReferenceRegisters;

/// <summary>
/// Ephemeral idempotency state for Independent-mode Reference Register writes.
///
/// One row per (register_id, command_id, operation) written in the same transaction as register records.
/// Allows safe retries on timeouts / crashes.
///
/// IMPORTANT:
/// - This table is technical mutable state, not immutable history.
/// - Immutable attempt history lives in reference_register_independent_write_log_history.
///
/// Operation is stored as SMALLINT:
/// 1 = Upsert, 2 = Tombstone
/// </summary>
public sealed class ReferenceRegisterIndependentWriteStateMigration : IDdlObject
{
    public string Name => "reference_register_independent_write_state";

    public string Generate() => """
                                DO $$
                                BEGIN
                                    IF to_regclass('public.reference_register_independent_write_state') IS NULL
                                       AND to_regclass('public.reference_register_independent_write_log') IS NOT NULL THEN
                                        ALTER TABLE reference_register_independent_write_log RENAME TO reference_register_independent_write_state;
                                    END IF;
                                END
                                $$;

                                CREATE TABLE IF NOT EXISTS reference_register_independent_write_state (
                                    register_id      uuid        NOT NULL,
                                    command_id       uuid        NOT NULL,
                                    operation        smallint    NOT NULL,

                                    attempt_id       uuid        NULL,
                                    started_at_utc   timestamptz NOT NULL,
                                    completed_at_utc timestamptz NULL,

                                    CONSTRAINT pk_refreg_independent_write_log
                                        PRIMARY KEY (register_id, command_id, operation),

                                    CONSTRAINT fk_refreg_ind_write_log_register
                                        FOREIGN KEY (register_id)
                                        REFERENCES reference_registers(register_id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT ck_refreg_ind_write_log_operation
                                        CHECK (operation IN (1, 2)),

                                    CONSTRAINT ck_refreg_ind_write_log_completed_after_started
                                        CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)
                                );

                                ALTER TABLE reference_register_independent_write_state
                                    ADD COLUMN IF NOT EXISTS attempt_id uuid;
                                """;
}
