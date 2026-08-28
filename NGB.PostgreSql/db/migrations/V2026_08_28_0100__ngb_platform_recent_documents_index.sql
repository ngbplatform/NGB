CREATE INDEX IF NOT EXISTS ix_documents_type_active_updated_id
    ON documents(type_code, updated_at_utc DESC, id DESC)
    WHERE status <> 3;
