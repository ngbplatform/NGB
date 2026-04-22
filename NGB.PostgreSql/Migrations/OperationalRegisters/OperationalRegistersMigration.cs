using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.OperationalRegisters;

/// <summary>
/// Operational registers registry (metadata).
///
/// Operational registers are document-driven and use append-only movements (with reversal on Unpost/Repost).
/// The registry is a platform table used for:
/// - stable FK target for per-register rules/finalizations/write-log
/// - displaying register names in diagnostics/UX
///
/// NOTE: this table is NOT append-only (name may change), but it is immutable enough for now.
/// We intentionally keep the lifecycle minimal (no is_deleted/is_active) until there is a real admin UX.
/// </summary>
public sealed class OperationalRegistersMigration : IDdlObject
{
    public string Name => "operational_registers";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS operational_registers (
                                    register_id UUID PRIMARY KEY,

                                    code TEXT NOT NULL,
                                    name TEXT NOT NULL,

                                    -- Once any movements have been written to a register, some metadata becomes immutable
                                    -- (e.g. resource physical column identifiers). This flag allows DB-level guards to
                                    -- enforce that invariant without scanning per-register tables.
                                    has_movements BOOLEAN NOT NULL DEFAULT FALSE,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT ck_operational_registers_code_nonempty CHECK (length(trim(code)) > 0),
                                    CONSTRAINT ck_operational_registers_name_nonempty CHECK (length(trim(name)) > 0)
                                );

                                -- Drift repair: CREATE TABLE IF NOT EXISTS won't add dropped columns / constraints.
                                -- Some drift-repair tests intentionally drop columns like register_id.
                                ALTER TABLE operational_registers
                                    ADD COLUMN IF NOT EXISTS register_id uuid,
                                    ADD COLUMN IF NOT EXISTS code text,
                                    ADD COLUMN IF NOT EXISTS name text,
                                    ADD COLUMN IF NOT EXISTS has_movements boolean NOT NULL DEFAULT FALSE,
                                    ADD COLUMN IF NOT EXISTS created_at_utc timestamptz NOT NULL DEFAULT NOW(),
                                    ADD COLUMN IF NOT EXISTS updated_at_utc timestamptz NOT NULL DEFAULT NOW();

                                DO $$
                                BEGIN
                                    -- Primary key (idempotent).
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_registers'::regclass
                                          AND contype = 'p'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_registers
                                                ADD CONSTRAINT pk_operational_registers
                                                    PRIMARY KEY (register_id)
                                        $ddl$;
                                    END IF;

                                    -- Non-empty constraints (idempotent).
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_registers'::regclass
                                          AND conname = 'ck_operational_registers_code_nonempty'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_registers
                                                ADD CONSTRAINT ck_operational_registers_code_nonempty
                                                    CHECK (length(trim(code)) > 0)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_registers'::regclass
                                          AND conname = 'ck_operational_registers_name_nonempty'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_registers
                                                ADD CONSTRAINT ck_operational_registers_name_nonempty
                                                    CHECK (length(trim(name)) > 0)
                                        $ddl$;
                                    END IF;

                                    -- Timestamp defaults (timestamptz instants use DEFAULT NOW()).
                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'operational_registers'
                                          AND column_name = 'created_at_utc'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_registers
                                                ALTER COLUMN created_at_utc SET DEFAULT NOW()
                                        $ddl$;
                                    END IF;

                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'operational_registers'
                                          AND column_name = 'updated_at_utc'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_registers
                                                ALTER COLUMN updated_at_utc SET DEFAULT NOW()
                                        $ddl$;
                                    END IF;
                                END$$;
                                """;
}
