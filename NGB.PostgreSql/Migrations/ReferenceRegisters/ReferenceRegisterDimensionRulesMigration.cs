using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.ReferenceRegisters;

/// <summary>
/// Key dimension rules for Reference Registers.
///
/// We re-use platform Dimensions and Dimension Sets:
/// - every record stores a <c>dimension_set_id</c> that points to platform_dimension_sets
/// - rules define which dimensions are allowed/required for this register
///
/// IMPORTANT:
/// - once a register has records, dimension rules become append-only (guarded by triggers in a later migration)
///   to avoid invalidating historical facts.
/// </summary>
public sealed class ReferenceRegisterDimensionRulesMigration : IDdlObject
{
    public string Name => "reference_register_dimension_rules";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS reference_register_dimension_rules
                                (
                                    register_id    uuid        NOT NULL,
                                    dimension_id   uuid        NOT NULL,
                                    ordinal        integer     NOT NULL,
                                    is_required    boolean     NOT NULL,
                                    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
                                    updated_at_utc timestamptz NOT NULL DEFAULT NOW(),

                                    CONSTRAINT pk_reference_register_dimension_rules
                                        PRIMARY KEY (register_id, dimension_id),

                                    CONSTRAINT ux_reference_register_dimension_rules__register_ordinal
                                        UNIQUE (register_id, ordinal),

                                    CONSTRAINT ck_reference_register_dimension_rules__ordinal_positive
                                        CHECK (ordinal > 0),

                                    CONSTRAINT fk_refreg_dim_rules__register
                                        FOREIGN KEY (register_id) REFERENCES reference_registers(register_id),

                                    CONSTRAINT fk_refreg_dim_rules__dimension
                                        FOREIGN KEY (dimension_id) REFERENCES platform_dimensions(dimension_id)
                                );

                                -- Drift repair for timestamp defaults.
                                DO $$
                                BEGIN
                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'reference_register_dimension_rules'
                                          AND column_name = 'created_at_utc'
                                    ) THEN
                                        ALTER TABLE reference_register_dimension_rules
                                            ALTER COLUMN created_at_utc SET DEFAULT NOW();
                                    END IF;

                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'reference_register_dimension_rules'
                                          AND column_name = 'updated_at_utc'
                                    ) THEN
                                        ALTER TABLE reference_register_dimension_rules
                                            ALTER COLUMN updated_at_utc SET DEFAULT NOW();
                                    END IF;
                                END$$;

                                -- Drift repair for critical foreign keys (CREATE TABLE IF NOT EXISTS will not restore dropped FKs).
                                DO $$
                                BEGIN
                                    IF to_regclass('public.reference_register_dimension_rules') IS NULL THEN
                                        RETURN;
                                    END IF;

                                    IF to_regclass('public.reference_registers') IS NULL OR to_regclass('public.platform_dimensions') IS NULL THEN
                                        RETURN;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint c
                                        JOIN pg_class t ON t.oid = c.conrelid
                                        JOIN pg_namespace n ON n.oid = t.relnamespace
                                        WHERE n.nspname = 'public'
                                          AND t.relname = 'reference_register_dimension_rules'
                                          AND c.conname = 'fk_refreg_dim_rules__register'
                                    ) THEN
                                        ALTER TABLE public.reference_register_dimension_rules
                                            ADD CONSTRAINT fk_refreg_dim_rules__register
                                                FOREIGN KEY (register_id) REFERENCES public.reference_registers(register_id);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint c
                                        JOIN pg_class t ON t.oid = c.conrelid
                                        JOIN pg_namespace n ON n.oid = t.relnamespace
                                        WHERE n.nspname = 'public'
                                          AND t.relname = 'reference_register_dimension_rules'
                                          AND c.conname = 'fk_refreg_dim_rules__dimension'
                                    ) THEN
                                        ALTER TABLE public.reference_register_dimension_rules
                                            ADD CONSTRAINT fk_refreg_dim_rules__dimension
                                                FOREIGN KEY (dimension_id) REFERENCES public.platform_dimensions(dimension_id);
                                    END IF;
                                END
                                $$;

                                """;
}
