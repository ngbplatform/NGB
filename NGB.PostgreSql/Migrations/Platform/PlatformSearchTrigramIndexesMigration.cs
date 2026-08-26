using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

/// <summary>
/// Enables indexed case-insensitive contains search used by platform and universal list readers.
/// </summary>
public sealed class PlatformSearchTrigramIndexesMigration : IDdlObject
{
    public string Name => "platform_search_trigram_indexes";

    public string Generate() => """
                                CREATE EXTENSION IF NOT EXISTS pg_trgm;

                                CREATE OR REPLACE FUNCTION ngb_install_search_trigram_indexes(table_prefixes text[])
                                RETURNS void
                                LANGUAGE plpgsql
                                SET search_path = pg_catalog, public
                                AS $function$
                                DECLARE
                                    target record;
                                    index_name text;
                                    trgm_schema text;
                                BEGIN
                                    SELECT n.nspname
                                      INTO STRICT trgm_schema
                                      FROM pg_extension e
                                      JOIN pg_namespace n ON n.oid = e.extnamespace
                                     WHERE e.extname = 'pg_trgm';

                                    FOR target IN
                                        SELECT c.table_name
                                          FROM information_schema.columns c
                                         WHERE c.table_schema = 'public'
                                           AND c.column_name = 'display'
                                           AND c.data_type IN ('text', 'character varying', 'character')
                                           AND EXISTS (
                                               SELECT 1
                                                 FROM unnest(table_prefixes) AS prefix(value)
                                                WHERE starts_with(c.table_name, prefix.value)
                                           )
                                         ORDER BY c.table_name
                                    LOOP
                                        index_name := format(
                                            'ix_%s_%s_display_trgm',
                                            left(target.table_name, 38),
                                            substr(md5(target.table_name), 1, 8));

                                        EXECUTE format(
                                            'CREATE INDEX IF NOT EXISTS %I ON public.%I USING gin (display %I.gin_trgm_ops)',
                                            index_name,
                                            target.table_name,
                                            trgm_schema);
                                    END LOOP;
                                END
                                $function$;

                                DO $indexes$
                                DECLARE
                                    trgm_schema text;
                                BEGIN
                                    SELECT n.nspname
                                      INTO STRICT trgm_schema
                                      FROM pg_extension e
                                      JOIN pg_namespace n ON n.oid = e.extnamespace
                                     WHERE e.extname = 'pg_trgm';

                                    EXECUTE format(
                                        'CREATE INDEX IF NOT EXISTS ix_documents_number_trgm ON public.documents USING gin (number %I.gin_trgm_ops) WHERE number IS NOT NULL',
                                        trgm_schema);
                                    EXECUTE format(
                                        'CREATE INDEX IF NOT EXISTS ix_accounting_accounts_code_trgm ON public.accounting_accounts USING gin (code %I.gin_trgm_ops)',
                                        trgm_schema);
                                    EXECUTE format(
                                        'CREATE INDEX IF NOT EXISTS ix_accounting_accounts_name_trgm ON public.accounting_accounts USING gin (name %I.gin_trgm_ops)',
                                        trgm_schema);
                                    EXECUTE format(
                                        'CREATE INDEX IF NOT EXISTS ix_doc_gje_reason_code_trgm ON public.doc_general_journal_entry USING gin (reason_code %I.gin_trgm_ops) WHERE reason_code IS NOT NULL',
                                        trgm_schema);
                                    EXECUTE format(
                                        'CREATE INDEX IF NOT EXISTS ix_doc_gje_memo_trgm ON public.doc_general_journal_entry USING gin (memo %I.gin_trgm_ops) WHERE memo IS NOT NULL',
                                        trgm_schema);
                                    EXECUTE format(
                                        'CREATE INDEX IF NOT EXISTS ix_doc_gje_external_reference_trgm ON public.doc_general_journal_entry USING gin (external_reference %I.gin_trgm_ops) WHERE external_reference IS NOT NULL',
                                        trgm_schema);
                                END
                                $indexes$;
                                """;
}
