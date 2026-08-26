CREATE INDEX IF NOT EXISTS ix_documents_type_posted_id
    ON documents(type_code, id)
    WHERE status = 2;
