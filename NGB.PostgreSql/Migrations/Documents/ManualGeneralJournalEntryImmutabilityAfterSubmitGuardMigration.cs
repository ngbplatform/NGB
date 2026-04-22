using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

public sealed class ManualGeneralJournalEntryImmutabilityAfterSubmitGuardMigration : IDdlObject
{
    public string Name => "documents.general_journal_entry.manual_immutability_after_submit_guard";

    public string Generate() => """
CREATE OR REPLACE FUNCTION ngb_forbid_mutation_of_manual_gje_business_fields_when_not_draft()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    doc_status smallint;
BEGIN
    -- If the document is already Posted, the generic posted immutability guard is the single source of truth.
    SELECT status INTO doc_status FROM documents WHERE id = COALESCE(NEW.document_id, OLD.document_id);
    IF COALESCE(doc_status, 0) = 2 THEN
        IF TG_OP = 'DELETE' THEN
            RETURN OLD;
        END IF;

        RETURN NEW;
    END IF;


    IF TG_OP <> 'UPDATE' THEN
        IF TG_OP = 'DELETE' THEN
            RETURN OLD;
        END IF;

        RETURN NEW;
    END IF;

    -- Only manual entries are governed by this guard.
    IF COALESCE(OLD.source, 0) <> 1 THEN
        RETURN NEW;
    END IF;

    -- Draft is editable.
    IF COALESCE(OLD.approval_state, 0) = 1 THEN
        RETURN NEW;
    END IF;

    -- Business fields must not change once submitted/approved/rejected.
    IF (NEW.journal_type IS DISTINCT FROM OLD.journal_type)
        OR (NEW.source IS DISTINCT FROM OLD.source)
        OR (NEW.reason_code IS DISTINCT FROM OLD.reason_code)
        OR (NEW.memo IS DISTINCT FROM OLD.memo)
        OR (NEW.external_reference IS DISTINCT FROM OLD.external_reference)
        OR (NEW.auto_reverse IS DISTINCT FROM OLD.auto_reverse)
        OR (NEW.auto_reverse_on_utc IS DISTINCT FROM OLD.auto_reverse_on_utc)
        OR (NEW.reversal_of_document_id IS DISTINCT FROM OLD.reversal_of_document_id)
        OR (NEW.initiated_by IS DISTINCT FROM OLD.initiated_by)
        OR (NEW.initiated_at_utc IS DISTINCT FROM OLD.initiated_at_utc)
    THEN
        RAISE EXCEPTION 'Manual GJE is immutable after submission: %', COALESCE(NEW.document_id, OLD.document_id)
            USING ERRCODE = '55000';
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION ngb_forbid_mutation_of_manual_gje_lines_when_not_draft()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    st smallint;
    src smallint;
    doc_status smallint;
    doc_id uuid;
BEGIN
    doc_id := COALESCE(NEW.document_id, OLD.document_id);

    SELECT d.status, g.approval_state, g.source
      INTO doc_status, st, src
      FROM documents d
      JOIN doc_general_journal_entry g ON g.document_id = d.id
     WHERE d.id = doc_id;

    IF COALESCE(doc_status, 0) = 2 THEN
        IF TG_OP = 'DELETE' THEN
            RETURN OLD;
        END IF;

        RETURN NEW;
    END IF;

    IF COALESCE(src, 0) = 1 AND COALESCE(st, 0) <> 1 THEN
        RAISE EXCEPTION 'Manual GJE lines are immutable after submission: %', doc_id
            USING ERRCODE = '55000';
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION ngb_forbid_mutation_of_manual_gje_allocations_when_submitted_or_rejected()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    st smallint;
    src smallint;
    doc_status smallint;
    doc_id uuid;
BEGIN
    doc_id := COALESCE(NEW.document_id, OLD.document_id);

    SELECT d.status, g.approval_state, g.source
      INTO doc_status, st, src
      FROM documents d
      JOIN doc_general_journal_entry g ON g.document_id = d.id
     WHERE d.id = doc_id;

    IF COALESCE(doc_status, 0) = 2 THEN
        IF TG_OP = 'DELETE' THEN
            RETURN OLD;
        END IF;

        RETURN NEW;
    END IF;

    -- Allocations are expected to be written during posting (Approved state).
    -- They must never be mutated for manual documents in Submitted/Rejected states.
    IF COALESCE(src, 0) = 1 AND COALESCE(st, 0) IN (2, 4) THEN
        RAISE EXCEPTION 'Manual GJE allocations are not allowed in state % for document %', st, doc_id
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
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_doc_gje_manual_header_immutable_after_submit') THEN
        CREATE TRIGGER trg_doc_gje_manual_header_immutable_after_submit
            BEFORE UPDATE ON doc_general_journal_entry
            FOR EACH ROW
            EXECUTE FUNCTION ngb_forbid_mutation_of_manual_gje_business_fields_when_not_draft();
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_doc_gje_manual_lines_immutable_after_submit') THEN
        CREATE TRIGGER trg_doc_gje_manual_lines_immutable_after_submit
            BEFORE INSERT OR UPDATE OR DELETE ON doc_general_journal_entry__lines
            FOR EACH ROW
            EXECUTE FUNCTION ngb_forbid_mutation_of_manual_gje_lines_when_not_draft();
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_doc_gje_manual_alloc_state_guard') THEN
        CREATE TRIGGER trg_doc_gje_manual_alloc_state_guard
            BEFORE INSERT OR UPDATE OR DELETE ON doc_general_journal_entry__allocations
            FOR EACH ROW
            EXECUTE FUNCTION ngb_forbid_mutation_of_manual_gje_allocations_when_submitted_or_rejected();
    END IF;
END
$$;

""";
}
