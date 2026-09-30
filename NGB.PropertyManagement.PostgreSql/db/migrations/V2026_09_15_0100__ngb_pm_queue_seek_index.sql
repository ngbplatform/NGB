-- evolve-tx-off
-- Supports the queue's deterministic descending date + document key order.
-- The older date-only index cannot order requests tied on the same date.
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_doc_pm_maintenance_request__queue_seek
    ON doc_pm_maintenance_request(requested_at_utc DESC, document_id DESC);
