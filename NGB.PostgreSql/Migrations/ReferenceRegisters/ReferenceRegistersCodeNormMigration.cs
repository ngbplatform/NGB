using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.ReferenceRegisters;

/// <summary>
/// Case-insensitive UX for reference_registers.code:
/// - Adds a generated normalized column: code_norm = lower(btrim(code))
/// - Enforces trimming constraints on code/name
///
/// Uniqueness is enforced by an index on code_norm (see indexes migration).
/// </summary>
public sealed class ReferenceRegistersCodeNormMigration : IDdlObject
{
    public string Name => "reference_registers_code_norm";

    public string Generate() => """
                                ALTER TABLE reference_registers
                                    ADD COLUMN IF NOT EXISTS code_norm TEXT GENERATED ALWAYS AS (lower(btrim(code))) STORED;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_reference_registers_code_trimmed'
                                    ) THEN
                                        ALTER TABLE reference_registers
                                            ADD CONSTRAINT ck_reference_registers_code_trimmed
                                            CHECK (code = btrim(code));
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_reference_registers_name_trimmed'
                                    ) THEN
                                        ALTER TABLE reference_registers
                                            ADD CONSTRAINT ck_reference_registers_name_trimmed
                                            CHECK (name = btrim(name));
                                    END IF;
                                END
                                $$;
                                """;
}
