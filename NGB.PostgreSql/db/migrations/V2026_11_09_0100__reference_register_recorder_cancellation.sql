-- Internal cancellation metadata belongs to a row, not to a business resource.
-- Existing history retains its original meaning; no old tombstones are reclassified.
CREATE OR REPLACE FUNCTION ngb_ensure_reference_register_recorder_cancellation(p_register_id uuid)
RETURNS void AS $$
DECLARE
    records_table text;
    relation_id regclass;
    column_info record;
BEGIN
    SELECT 'refreg_' || table_code || '__records'
    INTO records_table
    FROM public.reference_registers
    WHERE register_id = p_register_id AND record_mode = 1 AND periodicity <> 0;

    IF records_table IS NULL THEN
        RETURN;
    END IF;
    relation_id := to_regclass(format('public.%I', records_table));
    IF relation_id IS NULL THEN
        RETURN;
    END IF;

    IF EXISTS (
        SELECT 1 FROM public.reference_register_fields
        WHERE register_id = p_register_id AND column_code = '__is_recorder_cancellation'
    ) THEN
        RAISE EXCEPTION 'RR table % has a business field conflicting with internal column __is_recorder_cancellation', records_table;
    END IF;

    SELECT a.atttypid, a.attnotnull,
           col_description(a.attrelid, a.attnum) AS description,
           pg_get_expr(d.adbin, d.adrelid) AS default_expression
    INTO column_info
    FROM pg_attribute a
    LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
    WHERE a.attrelid = relation_id AND a.attname = '__is_recorder_cancellation'
      AND NOT a.attisdropped;

    IF FOUND THEN
        IF column_info.description IS DISTINCT FROM 'Platform-managed reference register recorder cancellation.'
           OR column_info.atttypid <> 'boolean'::regtype
           OR NOT column_info.attnotnull
           OR column_info.default_expression IS DISTINCT FROM 'false' THEN
            RAISE EXCEPTION 'RR table % has an incompatible or unowned column __is_recorder_cancellation; inspect its definition before upgrading', records_table;
        END IF;
    ELSE
        EXECUTE format(
            'ALTER TABLE public.%I ADD COLUMN __is_recorder_cancellation boolean NOT NULL DEFAULT false',
            records_table);
        EXECUTE format(
            'COMMENT ON COLUMN public.%I.__is_recorder_cancellation IS %L',
            records_table, 'Platform-managed reference register recorder cancellation.');
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = relation_id AND conname = 'ck_refreg_recorder_cancellation'
    ) THEN
        EXECUTE format(
            'ALTER TABLE public.%I ADD CONSTRAINT ck_refreg_recorder_cancellation CHECK (NOT __is_recorder_cancellation OR is_deleted)',
            records_table);
    END IF;
END;
$$ LANGUAGE plpgsql;

DO $$
DECLARE
    item record;
BEGIN
    FOR item IN
        SELECT register_id FROM public.reference_registers
        WHERE record_mode = 1 AND periodicity <> 0
        ORDER BY register_id
    LOOP
        PERFORM ngb_ensure_reference_register_recorder_cancellation(item.register_id);
    END LOOP;
END $$;
