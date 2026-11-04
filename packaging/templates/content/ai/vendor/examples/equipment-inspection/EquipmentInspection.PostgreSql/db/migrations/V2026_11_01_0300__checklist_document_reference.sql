-- Hybrid part metadata requires a direct registry reference in addition to
-- the existing head reference that owns cascading Draft cleanup.
ALTER TABLE doc_example_checklist_checks
    ADD CONSTRAINT fk_example_checklist_checks_document
    FOREIGN KEY (document_id) REFERENCES documents(id);
