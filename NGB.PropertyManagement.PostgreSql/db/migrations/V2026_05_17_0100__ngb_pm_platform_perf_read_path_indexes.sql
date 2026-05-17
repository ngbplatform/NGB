-- PM-owned read-path indexes for platform performance workloads.
-- Keep vertical table knowledge here; the platform reader remains vertical-neutral.

CREATE INDEX IF NOT EXISTS ix_cat_pm_property__display_catalog_id
    ON cat_pm_property(display, catalog_id);

CREATE INDEX IF NOT EXISTS ix_cat_pm_party__display_catalog_id
    ON cat_pm_party(display, catalog_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_rent_charge__display_document_id
    ON doc_pm_rent_charge(display, document_id)
    INCLUDE (due_on_utc, lease_id, period_from_utc, period_to_utc, amount);

CREATE INDEX IF NOT EXISTS ix_doc_pm_rent_charge__due_display_document
    ON doc_pm_rent_charge(due_on_utc, display, document_id)
    INCLUDE (lease_id, period_from_utc, period_to_utc, amount);

CREATE INDEX IF NOT EXISTS ix_doc_pm_maintenance_request__display_document_id
    ON doc_pm_maintenance_request(display, document_id)
    INCLUDE (requested_at_utc, property_id, party_id, category_id, priority);
