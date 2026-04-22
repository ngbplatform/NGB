using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.ReferenceRegisters;

/// <summary>
/// Reference Register fields.
///
/// Fields define:
/// - which physical columns exist in per-register __records tables
/// - their stable column identifiers (<c>column_code</c>) used as UNQUOTED identifiers in dynamic DDL/DML
///
/// We store both:
/// - <c>code_norm</c>: lower(trim(code)) for business-level uniqueness
/// - <c>column_code</c>: safe SQL identifier derived from code (see runtime normalizer)
///
/// IMPORTANT:
/// - <c>column_code</c> must be unique within a register, because it becomes a physical column name.
/// - <c>column_code</c> must not conflict with per-register fact-table base columns.
///
/// NOTE:
/// We keep DB-level drift-repair blocks because migrations are idempotent (CREATE IF NOT EXISTS)
/// and test fixtures may reuse the same database across runs.
/// </summary>
public sealed class ReferenceRegisterFieldsMigration : IDdlObject
{
    public string Name => "reference_register_fields";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS reference_register_fields
                                (
                                    register_id       uuid        NOT NULL,
                                    code              text        NOT NULL,
                                    code_norm         text        NOT NULL,
                                    column_code       text        NOT NULL,
                                    name              text        NOT NULL,
                                    ordinal           integer     NOT NULL,
                                    column_type       smallint    NOT NULL,
                                    is_nullable       boolean     NOT NULL,
                                    created_at_utc    timestamptz NOT NULL DEFAULT NOW(),
                                    updated_at_utc    timestamptz NOT NULL DEFAULT NOW(),

                                    CONSTRAINT pk_reference_register_fields
                                        PRIMARY KEY (register_id, column_code),

                                    CONSTRAINT ux_reference_register_fields__register_code_norm
                                        UNIQUE (register_id, code_norm),

                                    CONSTRAINT ux_reference_register_fields__register_ordinal
                                        UNIQUE (register_id, ordinal),

                                    CONSTRAINT ck_reference_register_fields__ordinal_positive
                                        CHECK (ordinal > 0),

                                    CONSTRAINT ck_reference_register_fields__code_norm
                                        CHECK (code_norm = lower(btrim(code))),

                                    -- column_code becomes an UNQUOTED SQL identifier in dynamic DDL/DML.
                                    -- PostgreSQL rules for unquoted identifiers: ^[a-z_][a-z0-9_]*$
                                    -- Identifier length limit is 63 bytes (ASCII => 63 chars).
                                    CONSTRAINT ck_reference_register_fields__column_code_safe
                                        CHECK (column_code ~ '^[a-z_][a-z0-9_]*$' AND length(column_code) > 0 AND length(column_code) <= 63),

                                    CONSTRAINT ck_reference_register_fields__column_code_not_reserved
                                        CHECK (column_code NOT IN ('record_id','period_utc','period_bucket_utc','dimension_set_id','recorder_document_id','recorded_at_utc','is_deleted','occurred_at_utc')),

                                    -- Allowed logical column types (mirrors NGB.Metadata.Base.ColumnType).
                                    CONSTRAINT ck_reference_register_fields__column_type
                                        CHECK (column_type IN (0, 1, 2, 3, 4, 5, 6, 7, 8)),

                                    CONSTRAINT fk_refreg_fields__register
                                        FOREIGN KEY (register_id) REFERENCES reference_registers(register_id)
                                );

                                -- Drift repair for timestamp defaults.
                                DO $$
                                BEGIN
                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'reference_register_fields'
                                          AND column_name = 'created_at_utc'
                                    ) THEN
                                        ALTER TABLE reference_register_fields
                                            ALTER COLUMN created_at_utc SET DEFAULT NOW();
                                    END IF;

                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'reference_register_fields'
                                          AND column_name = 'updated_at_utc'
                                    ) THEN
                                        ALTER TABLE reference_register_fields
                                            ALTER COLUMN updated_at_utc SET DEFAULT NOW();
                                    END IF;
                                END$$;

                                -- Drift repair for ck_reference_register_fields__column_code_not_reserved.
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
                                       AND t.relname = 'reference_register_fields'
                                       AND c.conname = 'ck_reference_register_fields__column_code_not_reserved';

                                    IF def IS NULL THEN
                                        ALTER TABLE reference_register_fields
                                            ADD CONSTRAINT ck_reference_register_fields__column_code_not_reserved
                                            CHECK (column_code NOT IN ('record_id','period_utc','period_bucket_utc','dimension_set_id','recorder_document_id','recorded_at_utc','is_deleted','occurred_at_utc'));
                                    END IF;
                                END$$;

                                -- DB-level immutability guard: once a register has records, field identifiers become immutable.
                                -- We allow updating only user-facing fields (name/ordinal) but forbid:
                                -- - DELETE
                                -- - changing code/code_norm/column_code/column_type/is_nullable
                                CREATE OR REPLACE FUNCTION ngb_refreg_forbid_field_mutation_when_has_records()
                                RETURNS trigger AS $$
                                DECLARE
                                    has_rec boolean;
                                BEGIN
                                    SELECT r.has_records
                                      INTO has_rec
                                      FROM reference_registers r
                                     WHERE r.register_id = OLD.register_id;

                                    IF COALESCE(has_rec, FALSE) THEN
                                        IF TG_OP = 'DELETE' THEN
                                            RAISE EXCEPTION 'Reference register fields are immutable after records exist.';
                                        END IF;

                                        IF TG_OP = 'UPDATE' THEN
                                            IF NEW.register_id IS DISTINCT FROM OLD.register_id
                                               OR NEW.code IS DISTINCT FROM OLD.code
                                               OR NEW.code_norm IS DISTINCT FROM OLD.code_norm
                                               OR NEW.column_code IS DISTINCT FROM OLD.column_code
                                               OR NEW.column_type IS DISTINCT FROM OLD.column_type
                                               OR NEW.is_nullable IS DISTINCT FROM OLD.is_nullable
                                            THEN
                                                RAISE EXCEPTION 'Reference register field identifiers are immutable after records exist.';
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
                                        WHERE tgname = 'trg_refreg_fields_immutable_when_has_records'
                                          AND tgrelid = 'reference_register_fields'::regclass
                                    ) THEN
                                        CREATE TRIGGER trg_refreg_fields_immutable_when_has_records
                                            BEFORE UPDATE OR DELETE
                                            ON reference_register_fields
                                            FOR EACH ROW
                                            EXECUTE FUNCTION ngb_refreg_forbid_field_mutation_when_has_records();
                                    END IF;
                                END$$;

                                -- Drift repair for fk_refreg_fields__register (CREATE TABLE IF NOT EXISTS will not restore dropped FKs).
                                DO $$
                                BEGIN
                                    IF to_regclass('public.reference_register_fields') IS NULL THEN
                                        RETURN;
                                    END IF;

                                    IF to_regclass('public.reference_registers') IS NULL THEN
                                        RETURN;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint c
                                        JOIN pg_class t ON t.oid = c.conrelid
                                        JOIN pg_namespace n ON n.oid = t.relnamespace
                                        WHERE n.nspname = 'public'
                                          AND t.relname = 'reference_register_fields'
                                          AND c.conname = 'fk_refreg_fields__register'
                                    ) THEN
                                        ALTER TABLE public.reference_register_fields
                                            ADD CONSTRAINT fk_refreg_fields__register
                                                FOREIGN KEY (register_id) REFERENCES public.reference_registers(register_id);
                                    END IF;
                                END
                                $$;

                                """;
}
