using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.ReferenceRegisters;

/// <summary>
/// Immutable technical history for independent reference register write attempts.
/// </summary>
public sealed class ReferenceRegisterIndependentWriteLogHistoryMigration : IDdlObject
{
    public string Name => "reference_register_independent_write_log_history";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS reference_register_independent_write_log_history (
                                    history_id       uuid         PRIMARY KEY,
                                    attempt_id       uuid         NOT NULL,
                                    register_id      uuid         NOT NULL,
                                    command_id       uuid         NOT NULL,
                                    operation        smallint     NOT NULL,
                                    event_kind       smallint     NOT NULL,
                                    occurred_at_utc  timestamptz  NOT NULL,

                                    CONSTRAINT fk_refreg_ind_write_log_history_register
                                        FOREIGN KEY (register_id)
                                        REFERENCES reference_registers(register_id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT ck_refreg_ind_write_log_history_operation
                                        CHECK (operation IN (1, 2)),

                                    CONSTRAINT ck_refreg_ind_write_log_history_event_kind
                                        CHECK (event_kind IN (1, 2, 3))
                                );

                                CREATE INDEX IF NOT EXISTS ix_refreg_ind_write_log_history_command_operation_occurred
                                    ON reference_register_independent_write_log_history(command_id, operation, occurred_at_utc DESC);

                                CREATE INDEX IF NOT EXISTS ix_refreg_ind_write_log_history_attempt
                                    ON reference_register_independent_write_log_history(attempt_id, occurred_at_utc);
                                

                                DO $$
                                BEGIN
                                    IF to_regclass('public.reference_register_independent_write_log_history') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_refreg_ind_write_log_history_append_only ON public.reference_register_independent_write_log_history;
                                        CREATE TRIGGER trg_refreg_ind_write_log_history_append_only
                                            BEFORE UPDATE OR DELETE ON public.reference_register_independent_write_log_history
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;
                                END
                                $$;
                                """;
}
