-- CRM can be migrated independently of the platform pack (for example by its
-- integration-test fixture), so this migration must establish its own helper.
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

SELECT ngb_install_search_trigram_indexes(ARRAY['cat_crm_', 'doc_crm_']);
