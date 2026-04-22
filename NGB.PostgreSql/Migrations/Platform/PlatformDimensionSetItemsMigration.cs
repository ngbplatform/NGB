using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

public sealed class PlatformDimensionSetItemsMigration : IDdlObject
{
    public string Name => "platform_dimension_set_items";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS platform_dimension_set_items
                                (
                                    dimension_set_id UUID NOT NULL,
                                    dimension_id     UUID NOT NULL,
                                    value_id         UUID NOT NULL,

                                    created_at_utc   TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT pk_platform_dimset_items PRIMARY KEY (dimension_set_id, dimension_id),

                                    CONSTRAINT ck_platform_dimset_items_set_nonempty
                                        CHECK (dimension_set_id <> '00000000-0000-0000-0000-000000000000'::uuid),
                                    CONSTRAINT ck_platform_dimset_items_value_nonempty
                                        CHECK (value_id <> '00000000-0000-0000-0000-000000000000'::uuid),

                                    CONSTRAINT fk_platform_dimset_items_set
                                        FOREIGN KEY (dimension_set_id)
                                        REFERENCES platform_dimension_sets(dimension_set_id)
                                        ON DELETE RESTRICT,
                                    CONSTRAINT fk_platform_dimset_items_dimension
                                        FOREIGN KEY (dimension_id)
                                        REFERENCES platform_dimensions(dimension_id)
                                        ON DELETE RESTRICT
                                );

                                -- Drift repair: if a test (or manual drift) drops FK constraints, CREATE TABLE IF NOT EXISTS won't restore them.
                                -- We keep this block cheap in the steady state by first checking for canonical constraint names.
                                DO $$
                                DECLARE
                                    has_set boolean;
                                    has_dimension boolean;
                                    r record;
                                BEGIN
                                    SELECT EXISTS(
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'platform_dimension_set_items'::regclass
                                          AND conname = 'fk_platform_dimset_items_set'
                                    ) INTO has_set;

                                    SELECT EXISTS(
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'platform_dimension_set_items'::regclass
                                          AND conname = 'fk_platform_dimset_items_dimension'
                                    ) INTO has_dimension;

                                    IF has_set AND has_dimension THEN
                                        RETURN;
                                    END IF;

                                    FOR r IN
                                        SELECT conname
                                        FROM pg_constraint
                                        WHERE conrelid = 'platform_dimension_set_items'::regclass
                                          AND contype = 'f'
                                    LOOP
                                        EXECUTE format('ALTER TABLE platform_dimension_set_items DROP CONSTRAINT IF EXISTS %I', r.conname);
                                    END LOOP;

                                    ALTER TABLE platform_dimension_set_items
                                        ADD CONSTRAINT fk_platform_dimset_items_set
                                            FOREIGN KEY (dimension_set_id)
                                            REFERENCES platform_dimension_sets(dimension_set_id)
                                            ON DELETE RESTRICT;

                                    ALTER TABLE platform_dimension_set_items
                                        ADD CONSTRAINT fk_platform_dimset_items_dimension
                                            FOREIGN KEY (dimension_id)
                                            REFERENCES platform_dimensions(dimension_id)
                                            ON DELETE RESTRICT;
                                END $$;
                                """;
}
