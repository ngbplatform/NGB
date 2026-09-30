CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Module migrations and drift-repair hooks use this helper after their typed
-- catalog/document tables exist. Index names stay below PostgreSQL's 63-byte limit.
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

CREATE INDEX IF NOT EXISTS ix_documents_type_posted_id
    ON documents(type_code, id)
    WHERE status = 2;

CREATE INDEX IF NOT EXISTS ix_documents_type_active_updated_id
    ON documents(type_code, updated_at_utc DESC, id DESC)
    WHERE status <> 3;

CREATE INDEX IF NOT EXISTS ix_refreg_write_state_completed_document_operation_register
    ON reference_register_write_state(document_id, operation, register_id)
    WHERE completed_at_utc IS NOT NULL;

-- These indexes duplicate primary-key or unique-constraint indexes byte for byte.
-- Keeping both copies adds write amplification, WAL volume, vacuum work, and disk usage
-- without adding a distinct access path.
DROP INDEX IF EXISTS public.ix_acc_balances_period_account;
DROP INDEX IF EXISTS public.ix_acc_turnovers_period_account;
DROP INDEX IF EXISTS public.ix_opreg_dim_rules_register_ordinal;
DROP INDEX IF EXISTS public.ix_opreg_finalizations_register_period;
DROP INDEX IF EXISTS public.ix_platform_audit_event_changes_event;
DROP INDEX IF EXISTS public.ix_refreg_dim_rules_register_ordinal;

-- Supports the stable display-name ordering used by the paged platform-user read path.
CREATE INDEX IF NOT EXISTS ix_platform_users_display_sort
    ON public.platform_users(lower(coalesce(display_name, email, auth_subject)), user_id);

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
        -- Use the same Hash8 suffix as the movements store; repair must not create a duplicate.
        index_name := format(
            'ix_opreg_occurred_move_%s',
            substr(md5(target.tablename || '|occurred_move'), 1, 8));

        EXECUTE format(
            'CREATE INDEX IF NOT EXISTS %I ON public.%I (occurred_at_utc, movement_id)',
            index_name,
            target.tablename);
    END LOOP;
END
$indexes$;

-- Finalization workers poll only Dirty or Blocked rows. Partial indexes keep those
-- queue scans proportional to outstanding work instead of the full history table.
CREATE INDEX IF NOT EXISTS ix_opreg_finalizations_dirty_queue
    ON public.operational_register_finalizations(dirty_since_utc, register_id, period)
    WHERE status = 2;

CREATE INDEX IF NOT EXISTS ix_opreg_finalizations_blocked_queue
    ON public.operational_register_finalizations(blocked_since_utc, register_id, period)
    WHERE status = 3;
