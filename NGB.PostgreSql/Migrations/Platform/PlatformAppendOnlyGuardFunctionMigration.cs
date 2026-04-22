using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

/// <summary>
/// Single source of truth for the generic append-only guard function.
///
/// This function is reused across platform append-only tables (e.g. AuditLog, Dimension Sets).
/// UPDATE/DELETE are forbidden to guarantee immutability.
/// </summary>
public sealed class PlatformAppendOnlyGuardFunctionMigration : IDdlObject
{
    public string Name => "platform_append_only_guard_function";

    public string Generate() => """
                                CREATE OR REPLACE FUNCTION ngb_forbid_mutation_of_append_only_table()
                                RETURNS trigger AS $$
                                BEGIN
                                    RAISE EXCEPTION 'Append-only table cannot be mutated: %', TG_TABLE_NAME
                                        USING ERRCODE = '55000';
                                END;
                                $$ LANGUAGE plpgsql;
                                """;
}
