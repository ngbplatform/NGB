using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.ReferenceRegisters;

/// <summary>
/// Reference Registers registry (metadata).
///
/// Reference Registers store time-effective facts.
/// This table is the stable FK target for per-register rules, fields and idempotency logs.
///
/// Notes:
/// - The registry itself is NOT append-only (name may change).
/// - Some metadata becomes immutable once any records are written to the register.
///   The <c>has_records</c> flag allows DB-level guards to enforce such invariants.
/// </summary>
public sealed class ReferenceRegistersMigration : IDdlObject
{
    public string Name => "reference_registers";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS reference_registers (
                                    register_id UUID PRIMARY KEY,

                                    code TEXT NOT NULL,
                                    name TEXT NOT NULL,

                                    -- 0 = NonPeriodic, 1 = Second, 2 = Day, 3 = Month, 4 = Quarter, 5 = Year
                                    periodicity SMALLINT NOT NULL,

                                    -- 0 = Independent, 1 = SubordinateToRecorder
                                    record_mode SMALLINT NOT NULL,

                                    -- Once any records exist, some metadata becomes immutable.
                                    has_records BOOLEAN NOT NULL DEFAULT FALSE,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT ck_reference_registers_code_nonempty CHECK (length(btrim(code)) > 0),
                                    CONSTRAINT ck_reference_registers_name_nonempty CHECK (length(btrim(name)) > 0),

                                    CONSTRAINT ck_reference_registers_periodicity CHECK (periodicity IN (0, 1, 2, 3, 4, 5)),
                                    CONSTRAINT ck_reference_registers_record_mode CHECK (record_mode IN (0, 1))
                                );

                                -- Drift repair: CREATE TABLE IF NOT EXISTS won't add new columns.
                                ALTER TABLE reference_registers
                                    ADD COLUMN IF NOT EXISTS has_records BOOLEAN NOT NULL DEFAULT FALSE;
                                """;
}
