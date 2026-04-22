using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.ReferenceRegisters;

/// <summary>
/// Adds a generated <c>table_code</c> column to <c>reference_registers</c>.
///
/// Physical per-register tables are named using strict ASCII-only normalization:
///   refreg_<table_code>__records
///
/// We enforce:
/// - table_code is non-empty
/// - contains only [a-z0-9_]
/// - length is <= 47 so that refreg_<table_code>__records fits into PostgreSQL identifier limit (63)
///
/// If the normalized token is too long, we truncate and append a deterministic md5-based hash suffix ("_<12 hex>")
/// to avoid collisions.
/// </summary>
public sealed class ReferenceRegistersTableCodeMigration : IDdlObject
{
    public string Name => "reference_registers_table_code";

    public string Generate() => """
                                ALTER TABLE reference_registers
                                    ADD COLUMN IF NOT EXISTS table_code TEXT GENERATED ALWAYS AS (
                                        (
                                            CASE
                                                WHEN length(
                                                    btrim(
                                                        regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                        '_'
                                                    )
                                                ) = 0 THEN ''

                                                WHEN length(
                                                    btrim(
                                                        regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                        '_'
                                                    )
                                                ) <= 47 THEN
                                                    btrim(
                                                        regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                        '_'
                                                    )

                                                ELSE
                                                    left(
                                                        btrim(
                                                            regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                            '_'
                                                        ),
                                                        34
                                                    )
                                                    || '_'
                                                    || substr(
                                                        md5(
                                                            btrim(
                                                                regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                                '_'
                                                            )
                                                        ),
                                                        1,
                                                        12
                                                    )
                                            END
                                        )
                                    ) STORED;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_reference_registers_table_code_nonempty'
                                    ) THEN
                                        ALTER TABLE reference_registers
                                            ADD CONSTRAINT ck_reference_registers_table_code_nonempty
                                            CHECK (length(table_code) > 0);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_reference_registers_table_code_safe'
                                    ) THEN
                                        ALTER TABLE reference_registers
                                            ADD CONSTRAINT ck_reference_registers_table_code_safe
                                            CHECK (table_code ~ '^[a-z0-9_]+$' AND length(table_code) > 0);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_reference_registers_table_code_len'
                                    ) THEN
                                        ALTER TABLE reference_registers
                                            ADD CONSTRAINT ck_reference_registers_table_code_len
                                            CHECK (length(table_code) <= 47);
                                    END IF;
                                END
                                $$;
                                """;
}
