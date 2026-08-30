using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.OperationalRegisters;

/// <summary>
/// Repairs the occurred-at cursor access path on existing dynamic movements tables.
/// Newly created tables receive the same index from the movements store schema contract.
/// </summary>
public sealed class OperationalRegisterMovementsPagingIndexesMigration : IDdlObject
{
    public string Name => "operational_register_movements_paging_indexes";

    public string Generate() => """
                                DO $indexes$
                                DECLARE
                                    target record;
                                    index_name text;
                                BEGIN
                                    FOR target IN
                                        SELECT tablename
                                          FROM pg_catalog.pg_tables
                                         WHERE schemaname = 'public'
                                           AND tablename LIKE 'opreg\_%\_\_movements' ESCAPE '\'
                                         ORDER BY tablename
                                    LOOP
                                        index_name := format(
                                            'ix_opreg_occurred_move_%s',
                                            substr(md5(target.tablename || '|occurred_move'), 1, 16));

                                        EXECUTE format(
                                            'CREATE INDEX IF NOT EXISTS %I ON public.%I (occurred_at_utc, movement_id)',
                                            index_name,
                                            target.tablename);
                                    END LOOP;
                                END
                                $indexes$;
                                """;
}
