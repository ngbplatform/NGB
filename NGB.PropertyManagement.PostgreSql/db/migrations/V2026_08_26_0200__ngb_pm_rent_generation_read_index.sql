-- Supports the keyset scan used by monthly rent-charge generation.
CREATE INDEX IF NOT EXISTS ix_doc_pm_lease__start_document
    ON doc_pm_lease(start_on_utc, document_id)
    INCLUDE (end_on_utc, rent_amount, due_day);
