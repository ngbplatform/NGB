using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

public sealed class GeneralJournalEntryMigration : IDdlObject
{
    public string Name => "documents.general_journal_entry";

    public string Generate() => """
CREATE TABLE IF NOT EXISTS doc_general_journal_entry
(
    document_id uuid PRIMARY KEY REFERENCES documents (id) ON DELETE CASCADE,
    journal_type smallint NOT NULL,
    source smallint NOT NULL,
    approval_state smallint NOT NULL,
    reason_code text NULL,
    memo text NULL,
    external_reference text NULL,
    auto_reverse boolean NOT NULL DEFAULT FALSE,
    auto_reverse_on_utc date NULL,
    reversal_of_document_id uuid NULL REFERENCES documents (id),
    initiated_by text NULL,
    initiated_at_utc timestamptz NULL,
    submitted_by text NULL,
    submitted_at_utc timestamptz NULL,
    approved_by text NULL,
    approved_at_utc timestamptz NULL,
    rejected_by text NULL,
    rejected_at_utc timestamptz NULL,
    reject_reason text NULL,
    posted_by text NULL,
    posted_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
    updated_at_utc timestamptz NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_doc_gje_journal_type CHECK (journal_type IN (1, 2, 3, 4, 5)),
    CONSTRAINT ck_doc_gje_source CHECK (source IN (1, 2)),
    CONSTRAINT ck_doc_gje_approval_state CHECK (approval_state IN (1, 2, 3, 4)),

    -- Once submitted (or beyond), a GJE must have business meaning in a stable form.
    CONSTRAINT ck_doc_gje_reason_memo_required CHECK (
        approval_state = 1 OR (reason_code IS NOT NULL AND memo IS NOT NULL)
    ),

    -- Auto reverse is only a setting on non-reversal entries.
    CONSTRAINT ck_doc_gje_auto_reverse_fields CHECK (
        (auto_reverse = FALSE AND auto_reverse_on_utc IS NULL)
        OR
        (auto_reverse = TRUE AND auto_reverse_on_utc IS NOT NULL AND reversal_of_document_id IS NULL)
    ),

    -- Reversal entries reference the original.
    CONSTRAINT ck_doc_gje_reversal_doc CHECK (
        reversal_of_document_id IS NULL
        OR (journal_type = 2 AND source = 2 AND auto_reverse = FALSE AND auto_reverse_on_utc IS NULL)
    ),

    -- System documents are always pre-approved (e.g. scheduled reversals).
    CONSTRAINT ck_doc_gje_system_is_approved CHECK (source <> 2 OR approval_state = 3),

    -- Approval state gates audit columns.
    CONSTRAINT ck_doc_gje_submission_state CHECK (
        (approval_state = 1 AND submitted_at_utc IS NULL AND submitted_by IS NULL AND approved_at_utc IS NULL AND approved_by IS NULL AND rejected_at_utc IS NULL AND rejected_by IS NULL AND reject_reason IS NULL)
        OR
        (approval_state = 2 AND submitted_at_utc IS NOT NULL AND submitted_by IS NOT NULL AND approved_at_utc IS NULL AND approved_by IS NULL AND rejected_at_utc IS NULL AND rejected_by IS NULL AND reject_reason IS NULL)
        OR
        (approval_state = 3 AND approved_at_utc IS NOT NULL AND approved_by IS NOT NULL AND rejected_at_utc IS NULL AND rejected_by IS NULL AND reject_reason IS NULL)
        OR
        (approval_state = 4 AND rejected_at_utc IS NOT NULL AND rejected_by IS NOT NULL AND reject_reason IS NOT NULL AND approved_at_utc IS NULL AND approved_by IS NULL)
    )
);

CREATE TABLE IF NOT EXISTS doc_general_journal_entry__lines
(
    document_id uuid NOT NULL,
    line_no int NOT NULL,
    side smallint NOT NULL,
    -- IMPORTANT: accounting_accounts PK is account_id, not id.
    account_id uuid NOT NULL REFERENCES accounting_accounts (account_id),
    amount numeric(19,4) NOT NULL,
    memo text NULL,
    dimension_set_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',


    -- Keep the header FK (defense in depth: lines cannot exist without header).
    CONSTRAINT fk_doc_gje_lines__header
        FOREIGN KEY (document_id) REFERENCES doc_general_journal_entry (document_id) ON DELETE CASCADE,

    -- Platform invariant: every typed table must be linked to documents(id).
    CONSTRAINT fk_doc_gje_lines__document
        FOREIGN KEY (document_id) REFERENCES documents (id),

    CONSTRAINT fk_doc_gje_lines__dimension_set
        FOREIGN KEY (dimension_set_id) REFERENCES platform_dimension_sets (dimension_set_id),

    PRIMARY KEY (document_id, line_no),
    CONSTRAINT ck_doc_gje_lines_side CHECK (side IN (1, 2)),
    CONSTRAINT ck_doc_gje_lines_amount CHECK (amount > 0)
);

CREATE TABLE IF NOT EXISTS doc_general_journal_entry__allocations
(
    document_id uuid NOT NULL,
    entry_no int NOT NULL,
    debit_line_no int NOT NULL,
    credit_line_no int NOT NULL,
    amount numeric(19,4) NOT NULL,

    -- Header FK keeps allocations tied to the GJE document.
    CONSTRAINT fk_doc_gje_alloc__header
        FOREIGN KEY (document_id) REFERENCES doc_general_journal_entry (document_id) ON DELETE CASCADE,

    -- Platform invariant: every typed table must be linked to documents(id).
    CONSTRAINT fk_doc_gje_alloc__document
        FOREIGN KEY (document_id) REFERENCES documents (id),

    PRIMARY KEY (document_id, entry_no),
    CONSTRAINT ck_doc_gje_alloc_amount CHECK (amount > 0),
    CONSTRAINT fk_doc_gje_alloc_debit FOREIGN KEY (document_id, debit_line_no) REFERENCES doc_general_journal_entry__lines (document_id, line_no) ON DELETE CASCADE,
    CONSTRAINT fk_doc_gje_alloc_credit FOREIGN KEY (document_id, credit_line_no) REFERENCES doc_general_journal_entry__lines (document_id, line_no) ON DELETE CASCADE
);

-- ----------------------------------------------------------
-- Drift repair (idempotent): ensure required FKs exist even
-- if tables were created by an older migration version.
-- ----------------------------------------------------------
DO $$
BEGIN
    -- Lines: ensure FK document_id -> documents(id) exists
    IF to_regclass('public.doc_general_journal_entry__lines') IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name
             AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema = tc.table_schema
            WHERE tc.table_schema = 'public'
              AND tc.table_name = 'doc_general_journal_entry__lines'
              AND tc.constraint_type = 'FOREIGN KEY'
              AND kcu.column_name = 'document_id'
              AND ccu.table_name = 'documents'
              AND ccu.column_name = 'id'
        ) THEN
            ALTER TABLE doc_general_journal_entry__lines
                ADD CONSTRAINT fk_doc_gje_lines__document
                    FOREIGN KEY (document_id) REFERENCES documents (id);
        END IF;


        -- Lines: ensure dimension_set_id column + FK exist
        IF NOT EXISTS (
            SELECT 1
            FROM information_schema.columns c
            WHERE c.table_schema = 'public'
              AND c.table_name = 'doc_general_journal_entry__lines'
              AND c.column_name = 'dimension_set_id'
        ) THEN
            ALTER TABLE doc_general_journal_entry__lines
                ADD COLUMN dimension_set_id uuid;

            UPDATE doc_general_journal_entry__lines
                SET dimension_set_id = '00000000-0000-0000-0000-000000000000'
            WHERE dimension_set_id IS NULL;

            ALTER TABLE doc_general_journal_entry__lines
                ALTER COLUMN dimension_set_id SET DEFAULT '00000000-0000-0000-0000-000000000000',
                ALTER COLUMN dimension_set_id SET NOT NULL;
        END IF;

        IF NOT EXISTS (
            SELECT 1
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name
             AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema = tc.table_schema
            WHERE tc.table_schema = 'public'
              AND tc.table_name = 'doc_general_journal_entry__lines'
              AND tc.constraint_type = 'FOREIGN KEY'
              AND kcu.column_name = 'dimension_set_id'
              AND ccu.table_name = 'platform_dimension_sets'
              AND ccu.column_name = 'dimension_set_id'
        ) THEN
            ALTER TABLE doc_general_journal_entry__lines
                ADD CONSTRAINT fk_doc_gje_lines__dimension_set
                    FOREIGN KEY (dimension_set_id) REFERENCES platform_dimension_sets (dimension_set_id);
        END IF;
    END IF;

    -- Allocations: ensure FK document_id -> documents(id) exists
    IF to_regclass('public.doc_general_journal_entry__allocations') IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name
             AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema = tc.table_schema
            WHERE tc.table_schema = 'public'
              AND tc.table_name = 'doc_general_journal_entry__allocations'
              AND tc.constraint_type = 'FOREIGN KEY'
              AND kcu.column_name = 'document_id'
              AND ccu.table_name = 'documents'
              AND ccu.column_name = 'id'
        ) THEN
            ALTER TABLE doc_general_journal_entry__allocations
                ADD CONSTRAINT fk_doc_gje_alloc__document
                    FOREIGN KEY (document_id) REFERENCES documents (id);
        END IF;
    END IF;
END $$;

-- ==========================================================
-- Posted document immutability guard (defense in depth)
-- ==========================================================
-- NOTE: These are platform invariants. We attach the immutability triggers
-- at the migration that creates the typed storages to avoid bootstrapping
-- drift (schema validation expects these guards to exist).

CREATE OR REPLACE FUNCTION ngb_forbid_mutation_of_posted_document()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    st smallint;
    doc_id uuid;
BEGIN
    doc_id := COALESCE(NEW.document_id, OLD.document_id);

    SELECT status INTO st
    FROM documents
    WHERE id = doc_id;

    -- DocumentStatus.Posted is expected to be 2 in the platform enum.
    IF COALESCE(st, 0) = 2 THEN
        RAISE EXCEPTION 'Document is posted and immutable: %', doc_id
            USING ERRCODE = '55000';
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_doc_gje_posted_immutable') THEN
        CREATE TRIGGER trg_doc_gje_posted_immutable
            BEFORE INSERT OR UPDATE OR DELETE ON doc_general_journal_entry
            FOR EACH ROW
            EXECUTE FUNCTION ngb_forbid_mutation_of_posted_document();
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_doc_gje_lines_posted_immutable') THEN
        CREATE TRIGGER trg_doc_gje_lines_posted_immutable
            BEFORE INSERT OR UPDATE OR DELETE ON doc_general_journal_entry__lines
            FOR EACH ROW
            EXECUTE FUNCTION ngb_forbid_mutation_of_posted_document();
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_doc_gje_alloc_posted_immutable') THEN
        CREATE TRIGGER trg_doc_gje_alloc_posted_immutable
            BEFORE INSERT OR UPDATE OR DELETE ON doc_general_journal_entry__allocations
            FOR EACH ROW
            EXECUTE FUNCTION ngb_forbid_mutation_of_posted_document();
    END IF;
END
$$;

""";
}
