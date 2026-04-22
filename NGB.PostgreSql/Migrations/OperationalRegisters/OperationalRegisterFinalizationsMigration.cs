using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.OperationalRegisters;

/// <summary>
/// Operational Registers finalization status.
///
/// This is NOT the accounting closed-period policy. For operational registers we use:
/// - Finalized: derived turnover/balance projections for the month are consistent and accepted.
/// - Dirty: some backdated movement was written and projections must be rebuilt/refinalized.
/// - BlockedNoProjector: finalization is blocked because no month projector is registered.
///
/// Primary key: (register_id, period)
/// where period is a month start (UTC).
/// </summary>
public sealed class OperationalRegisterFinalizationsMigration : IDdlObject
{
    public string Name => "operational_register_finalizations";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS operational_register_finalizations (
                                    register_id UUID NOT NULL,
                                    period DATE NOT NULL,

                                    status SMALLINT NOT NULL,
                                    finalized_at_utc TIMESTAMPTZ NULL,
                                    dirty_since_utc TIMESTAMPTZ NULL,
                                    blocked_since_utc TIMESTAMPTZ NULL,
                                    blocked_reason TEXT NULL,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT pk_opreg_finalizations PRIMARY KEY (register_id, period),

                                    CONSTRAINT fk_opreg_finalizations_register
                                        FOREIGN KEY (register_id)
                                        REFERENCES operational_registers(register_id)
                                        ON DELETE CASCADE,

                                    -- 1 = Finalized, 2 = Dirty, 3 = BlockedNoProjector
                                    CONSTRAINT ck_opreg_finalizations_status
                                        CHECK (status IN (1, 2, 3)),

                                    -- month start invariant
                                    CONSTRAINT ck_opreg_finalizations_period_is_month_start
                                        CHECK (date_trunc('month', period::timestamp)::date = period),

                                    CONSTRAINT ck_opreg_finalizations_consistent_timestamps
                                        CHECK (
                                            (status = 1 AND finalized_at_utc IS NOT NULL AND dirty_since_utc IS NULL AND blocked_since_utc IS NULL AND blocked_reason IS NULL)
                                            OR
                                            (status = 2 AND dirty_since_utc IS NOT NULL AND finalized_at_utc IS NULL AND blocked_since_utc IS NULL AND blocked_reason IS NULL)
                                            OR
                                            (status = 3 AND blocked_since_utc IS NOT NULL AND blocked_reason IS NOT NULL AND finalized_at_utc IS NULL AND dirty_since_utc IS NULL)
                                        )
                                );

                                -- Drift repair: CREATE TABLE IF NOT EXISTS doesn't restore dropped columns.
                                ALTER TABLE operational_register_finalizations
                                    ADD COLUMN IF NOT EXISTS register_id uuid,
                                    ADD COLUMN IF NOT EXISTS period date,
                                    ADD COLUMN IF NOT EXISTS status smallint;


                                DO $$
                                BEGIN
                                    -- Drift repair: ensure required columns exist before constraints.
                                    -- (Some test cases drop columns; keeping this inside the DO block avoids multi-statement parse/plan ordering issues.)
                                    EXECUTE $ddl$
                                        ALTER TABLE operational_register_finalizations
                                            ADD COLUMN IF NOT EXISTS register_id uuid,
                                            ADD COLUMN IF NOT EXISTS period date,
                                            ADD COLUMN IF NOT EXISTS status smallint
                                    $ddl$;

                                    -- Drift-repair: CREATE TABLE IF NOT EXISTS will not re-create dropped constraints.

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_finalizations'::regclass
                                          AND contype = 'p'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_finalizations
                                                ADD CONSTRAINT pk_opreg_finalizations PRIMARY KEY (register_id, period)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_finalizations'::regclass
                                          AND conname = 'fk_opreg_finalizations_register'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_finalizations
                                                ADD CONSTRAINT fk_opreg_finalizations_register
                                                    FOREIGN KEY (register_id)
                                                    REFERENCES operational_registers(register_id)
                                                    ON DELETE CASCADE
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_finalizations'::regclass
                                          AND conname = 'ck_opreg_finalizations_status'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_finalizations
                                                ADD CONSTRAINT ck_opreg_finalizations_status
                                                    CHECK (status IN (1, 2, 3))
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_finalizations'::regclass
                                          AND conname = 'ck_opreg_finalizations_period_is_month_start'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_finalizations
                                                ADD CONSTRAINT ck_opreg_finalizations_period_is_month_start
                                                    CHECK (date_trunc('month', period::timestamp)::date = period)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_finalizations'::regclass
                                          AND conname = 'ck_opreg_finalizations_consistent_timestamps'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_finalizations
                                                ADD CONSTRAINT ck_opreg_finalizations_consistent_timestamps
                                                    CHECK (
                                                        (status = 1 AND finalized_at_utc IS NOT NULL AND dirty_since_utc IS NULL AND blocked_since_utc IS NULL AND blocked_reason IS NULL)
                                                        OR
                                                        (status = 2 AND dirty_since_utc IS NOT NULL AND finalized_at_utc IS NULL AND blocked_since_utc IS NULL AND blocked_reason IS NULL)
                                                        OR
                                                        (status = 3 AND blocked_since_utc IS NOT NULL AND blocked_reason IS NOT NULL AND finalized_at_utc IS NULL AND dirty_since_utc IS NULL)
                                                    )
                                        $ddl$;
                                    END IF;
                                END
                                $$;
                                """;
}
