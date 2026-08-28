CREATE INDEX IF NOT EXISTS ix_reference_register_write_state_completed_document_operation_register
    ON reference_register_write_state(document_id, operation, register_id)
    WHERE completed_at_utc IS NOT NULL;
