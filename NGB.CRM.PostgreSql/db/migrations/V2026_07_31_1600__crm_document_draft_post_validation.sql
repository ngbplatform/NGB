-- CRM documents are edited draft-first. Conditional business requirements are
-- enforced at the Draft -> Posted boundary by the CRM post validators so that
-- incomplete drafts can be saved and corrected without surfacing PostgreSQL
-- constraint failures as HTTP 500 responses.

-- A conversion derived from a qualified lead does not know the account and
-- contact yet. Both values remain mandatory when the document is posted.
ALTER TABLE doc_crm_lead_conversion
    ALTER COLUMN account_id DROP NOT NULL,
    ALTER COLUMN contact_id DROP NOT NULL;

-- These constraints represent conditional posting rules rather than storage
-- invariants. Their matching post validators remain the single authoritative
-- source for user-facing business validation.
ALTER TABLE doc_crm_lead_qualification
    DROP CONSTRAINT IF EXISTS ck_doc_crm_lead_qualification__disqualification_reason;

ALTER TABLE doc_crm_lead_conversion
    DROP CONSTRAINT IF EXISTS ck_doc_crm_lead_conversion__opportunity_name,
    DROP CONSTRAINT IF EXISTS ck_doc_crm_lead_conversion__stage;

ALTER TABLE doc_crm_opportunity_update
    DROP CONSTRAINT IF EXISTS ck_doc_crm_opportunity_update__loss_reason;

ALTER TABLE doc_crm_quote
    DROP CONSTRAINT IF EXISTS ck_doc_crm_quote__valid_until;

ALTER TABLE doc_crm_activity_log
    DROP CONSTRAINT IF EXISTS ck_doc_crm_activity_log__target;
