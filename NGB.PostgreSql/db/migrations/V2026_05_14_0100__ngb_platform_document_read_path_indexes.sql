CREATE INDEX IF NOT EXISTS ix_documents_type_active_id
    ON documents(type_code, id)
    WHERE status <> 3;

CREATE INDEX IF NOT EXISTS ix_documents_type_active_date_id
    ON documents(type_code, date_utc, id)
    WHERE status <> 3;
