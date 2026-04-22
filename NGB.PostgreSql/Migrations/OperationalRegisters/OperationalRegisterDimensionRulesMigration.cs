using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.OperationalRegisters;

/// <summary>
/// Operational Register analytical dimension rules.
///
/// Each row declares that movements for a register MAY/MUST carry a value for a given platform dimension.
/// Dimension values are stored via DimensionSetId (platform_dimension_sets/items). These rules are used
/// by writers/validators and by readers for enriching/filtering.
/// </summary>
public sealed class OperationalRegisterDimensionRulesMigration : IDdlObject
{
    public string Name => "operational_register_dimension_rules";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS operational_register_dimension_rules (
                                    register_id UUID NOT NULL,
                                    dimension_id UUID NOT NULL,

                                    ordinal INT4 NOT NULL,
                                    is_required BOOLEAN NOT NULL DEFAULT FALSE,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT pk_opreg_dim_rules PRIMARY KEY (register_id, dimension_id),

                                    CONSTRAINT ux_opreg_dim_rules__register_ordinal
                                        UNIQUE (register_id, ordinal),

                                    CONSTRAINT fk_opreg_dim_rules_register
                                        FOREIGN KEY (register_id)
                                        REFERENCES operational_registers(register_id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT fk_opreg_dim_rules_dimension
                                        FOREIGN KEY (dimension_id)
                                        REFERENCES platform_dimensions(dimension_id),

                                    CONSTRAINT ck_opreg_dim_rules_ordinal_positive CHECK (ordinal > 0)
                                );

                                -- Drift repair: CREATE TABLE IF NOT EXISTS doesn't restore dropped columns.
                                ALTER TABLE operational_register_dimension_rules
                                    ADD COLUMN IF NOT EXISTS register_id uuid,
                                    ADD COLUMN IF NOT EXISTS dimension_id uuid,
                                    ADD COLUMN IF NOT EXISTS ordinal int4,
                                    ADD COLUMN IF NOT EXISTS is_required boolean;


                                DO $$
                                BEGIN
                                    -- Drift repair: ensure required columns exist before constraints.
                                    -- (Some test cases drop columns; keeping this inside the DO block avoids multi-statement parse/plan ordering issues.)
                                    EXECUTE $ddl$
                                        ALTER TABLE operational_register_dimension_rules
                                            ADD COLUMN IF NOT EXISTS register_id uuid,
                                            ADD COLUMN IF NOT EXISTS dimension_id uuid,
                                            ADD COLUMN IF NOT EXISTS ordinal int4,
                                            ADD COLUMN IF NOT EXISTS is_required boolean
                                    $ddl$;

                                    -- Drift-repair: CREATE TABLE IF NOT EXISTS will not re-create dropped constraints.

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_dimension_rules'::regclass
                                          AND contype = 'p'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_dimension_rules
                                                ADD CONSTRAINT pk_opreg_dim_rules PRIMARY KEY (register_id, dimension_id)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_dimension_rules'::regclass
                                          AND conname = 'ux_opreg_dim_rules__register_ordinal'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_dimension_rules
                                                ADD CONSTRAINT ux_opreg_dim_rules__register_ordinal
                                                    UNIQUE (register_id, ordinal)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_dimension_rules'::regclass
                                          AND conname = 'fk_opreg_dim_rules_register'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_dimension_rules
                                                ADD CONSTRAINT fk_opreg_dim_rules_register
                                                    FOREIGN KEY (register_id)
                                                    REFERENCES operational_registers(register_id)
                                                    ON DELETE CASCADE
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_dimension_rules'::regclass
                                          AND conname = 'fk_opreg_dim_rules_dimension'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_dimension_rules
                                                ADD CONSTRAINT fk_opreg_dim_rules_dimension
                                                    FOREIGN KEY (dimension_id)
                                                    REFERENCES platform_dimensions(dimension_id)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_dimension_rules'::regclass
                                          AND conname = 'ck_opreg_dim_rules_ordinal_positive'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_dimension_rules
                                                ADD CONSTRAINT ck_opreg_dim_rules_ordinal_positive
                                                    CHECK (ordinal > 0)
                                        $ddl$;
                                    END IF;
                                END
                                $$;
                                """;
}
