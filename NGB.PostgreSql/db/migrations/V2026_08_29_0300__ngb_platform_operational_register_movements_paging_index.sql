-- Existing operational-register movement tables are dynamic and therefore are not
-- covered by a static CREATE INDEX statement. Install the occurred-at paging access
-- path on every existing table; the runtime schema contract handles future tables.
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
