using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.OperationalRegisters;

/// <summary>
/// Adds a generated <c>table_code</c> column to <c>operational_registers</c>.
///
/// Why:
/// - Physical per-register tables are named using a strict ASCII-only normalization (see OperationalRegisterNaming).
/// - Different <c>code_norm</c> values can normalize to the same physical table name token (e.g. "a-b" and "a_b" => "a_b").
/// - Long codes may exceed PostgreSQL identifier limit (63 chars) and would be silently truncated by PostgreSQL,
///   which can merge unrelated registers into the same physical table.
///
/// This migration introduces <c>table_code</c> and enforces:
/// - <c>table_code</c> is non-empty
/// - <c>table_code</c> contains only [a-z0-9_]
/// - <c>table_code</c> length is <= 46 so that <c>opreg_&lt;table_code&gt;__movements</c> fits into the 63-char limit
///
/// Implementation notes:
/// - When the normalized token is too long, we truncate and append a deterministic md5-based hash suffix
///   ("_<12 hex>") to avoid prefix collisions.
/// </summary>
public sealed class OperationalRegistersTableCodeMigration : IDdlObject
{
    public string Name => "operational_registers_table_code";

    public string Generate() => """
                                ALTER TABLE operational_registers
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
                                                ) <= 46 THEN
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
                                                        33
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
                                        WHERE conname = 'ck_operational_registers_table_code_nonempty'
                                    ) THEN
                                        ALTER TABLE operational_registers
                                            ADD CONSTRAINT ck_operational_registers_table_code_nonempty
                                            CHECK (length(table_code) > 0);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_operational_registers_table_code_safe'
                                    ) THEN
                                        ALTER TABLE operational_registers
                                            ADD CONSTRAINT ck_operational_registers_table_code_safe
                                            CHECK (table_code ~ '^[a-z0-9_]+$' AND length(table_code) > 0);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_operational_registers_table_code_len'
                                    ) THEN
                                        ALTER TABLE operational_registers
                                            ADD CONSTRAINT ck_operational_registers_table_code_len
                                            CHECK (length(table_code) <= 46);
                                    END IF;
                                END
                                $$;
                                """;
}
