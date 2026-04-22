using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.OperationalRegisters;

/// <summary>
/// Operational Register resources (aka "measures") metadata.
///
/// Resources define:
/// - which numeric columns exist in per-register movements/turnovers/balances tables
/// - their stable column identifiers (<c>column_code</c>)
///
/// We store both:
/// - <c>code_norm</c>: lower(trim(code)) for business uniqueness
/// - <c>column_code</c>: safe SQL identifier derived from code (see OperationalRegisterNaming.NormalizeColumnCode)
///
/// IMPORTANT:
/// - <c>column_code</c> must be unique within a register, because it becomes a physical column name.
/// - different user-facing codes MAY normalize to the same <c>column_code</c>; we fail-fast.
/// - <c>column_code</c> must not conflict with per-register fact-table base columns.
///
/// NOTE:
/// We keep DB-level drift-repair blocks because migrations are idempotent (CREATE IF NOT EXISTS)
/// and test fixtures may reuse the same database across runs.
/// </summary>
public sealed class OperationalRegisterResourcesMigration : IDdlObject
{
    public string Name => "operational_register_resources";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS operational_register_resources
                                (
                                    register_id       uuid        NOT NULL,
                                    code              text        NOT NULL,
                                    code_norm         text        NOT NULL,
                                    column_code       text        NOT NULL,
                                    name              text        NOT NULL,
                                    ordinal           integer     NOT NULL,
                                    created_at_utc    timestamptz NOT NULL DEFAULT NOW(),
                                    updated_at_utc    timestamptz NOT NULL DEFAULT NOW(),

                                    CONSTRAINT pk_operational_register_resources
                                        PRIMARY KEY (register_id, column_code),

                                    CONSTRAINT ux_operational_register_resources__register_code_norm
                                        UNIQUE (register_id, code_norm),

                                    CONSTRAINT ux_operational_register_resources__register_ordinal
                                        UNIQUE (register_id, ordinal),

                                    CONSTRAINT ck_operational_register_resources__ordinal_positive
                                        CHECK (ordinal > 0),

                                    CONSTRAINT ck_operational_register_resources__code_norm
                                        CHECK (code_norm = lower(btrim(code))),

                                    -- column_code becomes an UNQUOTED SQL identifier in dynamic DDL/DML.
                                    -- PostgreSQL rules for unquoted identifiers: ^[a-z_][a-z0-9_]*$
                                    -- Identifier length limit is 63 bytes (ASCII => 63 chars).
                                    CONSTRAINT ck_operational_register_resources__column_code_safe
                                        CHECK (column_code ~ '^[a-z_][a-z0-9_]*$' AND length(column_code) > 0 AND length(column_code) <= 63),

                                    CONSTRAINT ck_operational_register_resources__column_code_not_reserved
                                        CHECK (column_code NOT IN ('movement_id','turnover_id','balance_id','document_id','occurred_at_utc','period_month','dimension_set_id','is_storno')),

                                    CONSTRAINT fk_opreg_resources__register
                                        FOREIGN KEY (register_id) REFERENCES operational_registers(register_id)
                                );

                                -- Drift repair: CREATE TABLE IF NOT EXISTS doesn't restore dropped columns.
                                ALTER TABLE operational_register_resources
                                    ADD COLUMN IF NOT EXISTS register_id uuid,
                                    ADD COLUMN IF NOT EXISTS code text,
                                    ADD COLUMN IF NOT EXISTS code_norm text,
                                    ADD COLUMN IF NOT EXISTS column_code text,
                                    ADD COLUMN IF NOT EXISTS name text,
                                    ADD COLUMN IF NOT EXISTS ordinal integer;


                                DO $$
                                BEGIN
                                    -- Drift repair: ensure required columns exist before constraints.
                                    -- (Some test cases drop columns; keeping this inside the DO block avoids multi-statement parse/plan ordering issues.)
                                    EXECUTE $ddl$
                                        ALTER TABLE operational_register_resources
                                            ADD COLUMN IF NOT EXISTS register_id uuid,
                                            ADD COLUMN IF NOT EXISTS code text,
                                            ADD COLUMN IF NOT EXISTS code_norm text,
                                            ADD COLUMN IF NOT EXISTS column_code text,
                                            ADD COLUMN IF NOT EXISTS name text,
                                            ADD COLUMN IF NOT EXISTS ordinal integer
                                    $ddl$;

                                    -- Drift-repair: CREATE TABLE IF NOT EXISTS will not re-create dropped constraints.

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_resources'::regclass
                                          AND contype = 'p'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ADD CONSTRAINT pk_operational_register_resources
                                                    PRIMARY KEY (register_id, column_code)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_resources'::regclass
                                          AND conname = 'ux_operational_register_resources__register_code_norm'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ADD CONSTRAINT ux_operational_register_resources__register_code_norm
                                                    UNIQUE (register_id, code_norm)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_resources'::regclass
                                          AND conname = 'ux_operational_register_resources__register_ordinal'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ADD CONSTRAINT ux_operational_register_resources__register_ordinal
                                                    UNIQUE (register_id, ordinal)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_resources'::regclass
                                          AND conname = 'fk_opreg_resources__register'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ADD CONSTRAINT fk_opreg_resources__register
                                                    FOREIGN KEY (register_id)
                                                    REFERENCES operational_registers(register_id)
                                        $ddl$;
                                    END IF;
                                END$$;

                                -- Drift repair for timestamp defaults (timestamptz instants use DEFAULT NOW()).
                                DO $$
                                BEGIN
                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'operational_register_resources'
                                          AND column_name = 'created_at_utc'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ALTER COLUMN created_at_utc SET DEFAULT NOW()
                                        $ddl$;
                                    END IF;

                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'operational_register_resources'
                                          AND column_name = 'updated_at_utc'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ALTER COLUMN updated_at_utc SET DEFAULT NOW()
                                        $ddl$;
                                    END IF;
                                END$$;

                                -- Drift repair for ck_operational_register_resources__column_code_safe.
                                DO $$
                                DECLARE
                                    def text;
                                BEGIN
                                    SELECT pg_get_constraintdef(c.oid)
                                      INTO def
                                      FROM pg_constraint c
                                      JOIN pg_class t ON t.oid = c.conrelid
                                      JOIN pg_namespace n ON n.oid = t.relnamespace
                                     WHERE n.nspname = 'public'
                                       AND t.relname = 'operational_register_resources'
                                       AND c.conname = 'ck_operational_register_resources__column_code_safe';

                                    IF def IS NULL THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ADD CONSTRAINT ck_operational_register_resources__column_code_safe
                                                    CHECK (column_code ~ '^[a-z_][a-z0-9_]*$' AND length(column_code) > 0 AND length(column_code) <= 63)
                                        $ddl$;
                                    ELSIF def NOT LIKE '%^[a-z_][a-z0-9_]*$%'
                                       OR def NOT LIKE '%<= 63%'
                                       OR def NOT LIKE '%length(column_code) > 0%'
                                    THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                DROP CONSTRAINT ck_operational_register_resources__column_code_safe
                                        $ddl$;

                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ADD CONSTRAINT ck_operational_register_resources__column_code_safe
                                                    CHECK (column_code ~ '^[a-z_][a-z0-9_]*$' AND length(column_code) > 0 AND length(column_code) <= 63)
                                        $ddl$;
                                    END IF;

                                    -- Drift repair for ck_operational_register_resources__column_code_not_reserved.
                                    SELECT pg_get_constraintdef(c.oid)
                                      INTO def
                                      FROM pg_constraint c
                                      JOIN pg_class t ON t.oid = c.conrelid
                                      JOIN pg_namespace n ON n.oid = t.relnamespace
                                     WHERE n.nspname = 'public'
                                       AND t.relname = 'operational_register_resources'
                                       AND c.conname = 'ck_operational_register_resources__column_code_not_reserved';

                                    IF def IS NULL THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ADD CONSTRAINT ck_operational_register_resources__column_code_not_reserved
                                                    CHECK (column_code NOT IN ('movement_id','turnover_id','balance_id','document_id','occurred_at_utc','period_month','dimension_set_id','is_storno'))
                                        $ddl$;
                                    END IF;
                                END$$;

                                -- DB-level immutability guard: once a register has movements, resource identifiers become immutable.
                                -- We allow updating only user-facing fields (name/ordinal) but forbid:
                                -- - DELETE
                                -- - changing code/code_norm/column_code
                                -- This protects reversal/storno semantics and dynamic schema safety even if callers bypass runtime services.
                                CREATE OR REPLACE FUNCTION ngb_opreg_forbid_resource_mutation_when_has_movements()
                                RETURNS trigger AS $$
                                DECLARE
                                    has_mov boolean;
                                BEGIN
                                    SELECT r.has_movements
                                      INTO has_mov
                                      FROM operational_registers r
                                     WHERE r.register_id = OLD.register_id;

                                    IF COALESCE(has_mov, FALSE) THEN
                                        IF TG_OP = 'DELETE' THEN
                                            RAISE EXCEPTION 'Operational register resources are immutable after movements exist.';
                                        END IF;

                                        IF TG_OP = 'UPDATE' THEN
                                            IF NEW.register_id IS DISTINCT FROM OLD.register_id
                                               OR NEW.code IS DISTINCT FROM OLD.code
                                               OR NEW.code_norm IS DISTINCT FROM OLD.code_norm
                                               OR NEW.column_code IS DISTINCT FROM OLD.column_code
                                            THEN
                                                RAISE EXCEPTION 'Operational register resource identifiers are immutable after movements exist.';
                                            END IF;
                                        END IF;
                                    END IF;

                                    IF TG_OP = 'DELETE' THEN
                                        RETURN OLD;
                                    END IF;

                                    RETURN NEW;
                                END;
                                $$ LANGUAGE plpgsql;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_trigger
                                        WHERE tgname = 'trg_opreg_resources_immutable_when_has_movements'
                                          AND tgrelid = 'operational_register_resources'::regclass
                                    ) THEN
                                        CREATE TRIGGER trg_opreg_resources_immutable_when_has_movements
                                            BEFORE UPDATE OR DELETE
                                            ON operational_register_resources
                                            FOR EACH ROW
                                            EXECUTE FUNCTION ngb_opreg_forbid_resource_mutation_when_has_movements();
                                    END IF;
                                END$$;
                                """;
}
