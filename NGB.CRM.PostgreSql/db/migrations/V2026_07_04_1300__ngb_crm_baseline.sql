-- NGB.CRM baseline schema.
--
-- Scope:
-- - typed CRM catalog and document tables
-- - deterministic read-side projections for leads, opportunities, quotes, and activities
-- - no accounting, inventory, invoicing, payroll, procurement, or external CRM API coupling

SET TIME ZONE 'UTC';
SET search_path = public;

-- -----------------------------------------------------------------------------
-- Catalogs
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS cat_crm_account (
    catalog_id       uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display          text NOT NULL,
    account_number   text NULL,
    name             text NOT NULL DEFAULT '',
    legal_name       text NULL,
    account_type     text NOT NULL DEFAULT 'Prospect',
    industry         text NULL,
    website          text NULL,
    phone            text NULL,
    email            text NULL,
    billing_address  text NULL,
    owner_user_id    uuid NULL REFERENCES platform_users(user_id) ON DELETE SET NULL,
    is_active        boolean NOT NULL DEFAULT true,
    notes            text NULL,

    CONSTRAINT ck_cat_crm_account__account_type
        CHECK (account_type IN ('Prospect', 'Customer', 'Partner', 'Vendor', 'Other')),
    CONSTRAINT ck_cat_crm_account__name_nonempty
        CHECK (length(btrim(name)) > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_crm_account__account_number
    ON cat_crm_account(account_number)
    WHERE account_number IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_cat_crm_account__display ON cat_crm_account(display);
CREATE INDEX IF NOT EXISTS ix_cat_crm_account__name ON cat_crm_account(name);
CREATE INDEX IF NOT EXISTS ix_cat_crm_account__account_type ON cat_crm_account(account_type);
CREATE INDEX IF NOT EXISTS ix_cat_crm_account__is_active ON cat_crm_account(is_active);

CREATE TABLE IF NOT EXISTS cat_crm_contact (
    catalog_id     uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display        text NOT NULL,
    account_id     uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    first_name     text NOT NULL DEFAULT '',
    last_name      text NOT NULL DEFAULT '',
    title          text NULL,
    email          text NULL,
    phone          text NULL,
    mobile_phone   text NULL,
    is_primary     boolean NOT NULL DEFAULT false,
    is_active      boolean NOT NULL DEFAULT true,
    notes          text NULL,

    CONSTRAINT ck_cat_crm_contact__name
        CHECK (length(btrim(first_name)) > 0 OR length(btrim(last_name)) > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_crm_contact__email
    ON cat_crm_contact(lower(email))
    WHERE email IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_cat_crm_contact__display ON cat_crm_contact(display);
CREATE INDEX IF NOT EXISTS ix_cat_crm_contact__account_id ON cat_crm_contact(account_id);
CREATE INDEX IF NOT EXISTS ix_cat_crm_contact__last_name ON cat_crm_contact(last_name);
CREATE INDEX IF NOT EXISTS ix_cat_crm_contact__is_active ON cat_crm_contact(is_active);

CREATE TABLE IF NOT EXISTS cat_crm_product (
    catalog_id       uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display          text NOT NULL,
    sku              text NULL,
    name             text NOT NULL DEFAULT '',
    family           text NULL,
    unit_of_measure  text NULL,
    list_price       numeric(18, 4) NULL,
    currency         text NULL,
    is_active        boolean NOT NULL DEFAULT true,
    notes            text NULL,

    CONSTRAINT ck_cat_crm_product__name_nonempty
        CHECK (length(btrim(name)) > 0),
    CONSTRAINT ck_cat_crm_product__list_price
        CHECK (list_price IS NULL OR list_price >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_crm_product__sku
    ON cat_crm_product(sku)
    WHERE sku IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_cat_crm_product__display ON cat_crm_product(display);
CREATE INDEX IF NOT EXISTS ix_cat_crm_product__family ON cat_crm_product(family);
CREATE INDEX IF NOT EXISTS ix_cat_crm_product__is_active ON cat_crm_product(is_active);

CREATE TABLE IF NOT EXISTS cat_crm_opportunity_stage (
    catalog_id            uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display               text NOT NULL,
    stage_code            text NOT NULL,
    name                  text NOT NULL DEFAULT '',
    ordinal               integer NOT NULL DEFAULT 0,
    default_probability   numeric(5, 2) NOT NULL DEFAULT 0,
    is_closed             boolean NOT NULL DEFAULT false,
    is_won                boolean NOT NULL DEFAULT false,
    is_active             boolean NOT NULL DEFAULT true,

    CONSTRAINT ck_cat_crm_opportunity_stage__stage_code
        CHECK (length(btrim(stage_code)) > 0),
    CONSTRAINT ck_cat_crm_opportunity_stage__probability
        CHECK (default_probability >= 0 AND default_probability <= 100),
    CONSTRAINT ck_cat_crm_opportunity_stage__closed_won
        CHECK (is_won = false OR is_closed = true)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_crm_opportunity_stage__stage_code
    ON cat_crm_opportunity_stage(stage_code);
CREATE INDEX IF NOT EXISTS ix_cat_crm_opportunity_stage__display ON cat_crm_opportunity_stage(display);
CREATE INDEX IF NOT EXISTS ix_cat_crm_opportunity_stage__ordinal ON cat_crm_opportunity_stage(ordinal);
CREATE INDEX IF NOT EXISTS ix_cat_crm_opportunity_stage__is_active ON cat_crm_opportunity_stage(is_active);

-- -----------------------------------------------------------------------------
-- Documents
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS doc_crm_lead_intake (
    document_id          uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display              text NULL,
    document_date_utc    date NOT NULL,
    lead_name            text NOT NULL,
    company_name         text NULL,
    contact_name         text NOT NULL,
    email                text NULL,
    phone                text NULL,
    lead_source          text NULL,
    industry             text NULL,
    estimated_value      numeric(18, 4) NULL,
    currency             text NULL,
    notes                text NULL,

    CONSTRAINT ck_doc_crm_lead_intake__lead_name CHECK (length(btrim(lead_name)) > 0),
    CONSTRAINT ck_doc_crm_lead_intake__contact_name CHECK (length(btrim(contact_name)) > 0),
    CONSTRAINT ck_doc_crm_lead_intake__estimated_value CHECK (estimated_value IS NULL OR estimated_value >= 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_crm_lead_intake__display ON doc_crm_lead_intake(display);
CREATE INDEX IF NOT EXISTS ix_doc_crm_lead_intake__document_date_utc ON doc_crm_lead_intake(document_date_utc);
CREATE INDEX IF NOT EXISTS ix_doc_crm_lead_intake__email ON doc_crm_lead_intake(email);
CREATE INDEX IF NOT EXISTS ix_doc_crm_lead_intake__company_name ON doc_crm_lead_intake(company_name);

CREATE TABLE IF NOT EXISTS doc_crm_lead_qualification (
    document_id                uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display                    text NULL,
    document_date_utc          date NOT NULL,
    lead_intake_id             uuid NOT NULL REFERENCES doc_crm_lead_intake(document_id) ON DELETE RESTRICT,
    qualification_state        text NOT NULL,
    score                      integer NOT NULL,
    disqualification_reason    text NULL,
    notes                      text NULL,

    CONSTRAINT ck_doc_crm_lead_qualification__state
        CHECK (qualification_state IN ('New', 'Qualified', 'Disqualified', 'Converted')),
    CONSTRAINT ck_doc_crm_lead_qualification__score
        CHECK (score >= 0 AND score <= 100),
    CONSTRAINT ck_doc_crm_lead_qualification__disqualification_reason
        CHECK (qualification_state <> 'Disqualified' OR length(btrim(coalesce(disqualification_reason, ''))) > 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_crm_lead_qualification__display ON doc_crm_lead_qualification(display);
CREATE INDEX IF NOT EXISTS ix_doc_crm_lead_qualification__lead_intake_id ON doc_crm_lead_qualification(lead_intake_id);
CREATE INDEX IF NOT EXISTS ix_doc_crm_lead_qualification__qualification_state ON doc_crm_lead_qualification(qualification_state);

CREATE TABLE IF NOT EXISTS doc_crm_lead_conversion (
    document_id             uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display                 text NULL,
    document_date_utc       date NOT NULL,
    lead_intake_id          uuid NOT NULL REFERENCES doc_crm_lead_intake(document_id) ON DELETE RESTRICT,
    account_id              uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    contact_id              uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    create_opportunity      boolean NOT NULL DEFAULT true,
    opportunity_name        text NULL,
    stage_id                uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    amount                  numeric(18, 4) NULL,
    probability             numeric(5, 2) NULL,
    expected_close_date     date NULL,
    currency                text NULL,
    notes                   text NULL,

    CONSTRAINT ck_doc_crm_lead_conversion__opportunity_name
        CHECK (create_opportunity = false OR length(btrim(coalesce(opportunity_name, ''))) > 0),
    CONSTRAINT ck_doc_crm_lead_conversion__stage
        CHECK (create_opportunity = false OR stage_id IS NOT NULL),
    CONSTRAINT ck_doc_crm_lead_conversion__amount
        CHECK (amount IS NULL OR amount >= 0),
    CONSTRAINT ck_doc_crm_lead_conversion__probability
        CHECK (probability IS NULL OR (probability >= 0 AND probability <= 100))
);

CREATE INDEX IF NOT EXISTS ix_doc_crm_lead_conversion__display ON doc_crm_lead_conversion(display);
CREATE INDEX IF NOT EXISTS ix_doc_crm_lead_conversion__lead_intake_id ON doc_crm_lead_conversion(lead_intake_id);
CREATE INDEX IF NOT EXISTS ix_doc_crm_lead_conversion__account_id ON doc_crm_lead_conversion(account_id);
CREATE INDEX IF NOT EXISTS ix_doc_crm_lead_conversion__contact_id ON doc_crm_lead_conversion(contact_id);
CREATE INDEX IF NOT EXISTS ix_doc_crm_lead_conversion__stage_id ON doc_crm_lead_conversion(stage_id);

CREATE TABLE IF NOT EXISTS doc_crm_opportunity_update (
    document_id             uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display                 text NULL,
    document_date_utc       date NOT NULL,
    opportunity_id          uuid NOT NULL REFERENCES doc_crm_lead_conversion(document_id) ON DELETE RESTRICT,
    stage_id                uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    amount                  numeric(18, 4) NOT NULL DEFAULT 0,
    probability             numeric(5, 2) NOT NULL DEFAULT 0,
    expected_close_date     date NULL,
    status                  text NOT NULL DEFAULT 'Open',
    loss_reason             text NULL,
    notes                   text NULL,

    CONSTRAINT ck_doc_crm_opportunity_update__amount CHECK (amount >= 0),
    CONSTRAINT ck_doc_crm_opportunity_update__probability CHECK (probability >= 0 AND probability <= 100),
    CONSTRAINT ck_doc_crm_opportunity_update__status CHECK (status IN ('Open', 'Won', 'Lost')),
    CONSTRAINT ck_doc_crm_opportunity_update__loss_reason
        CHECK (status <> 'Lost' OR length(btrim(coalesce(loss_reason, ''))) > 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_crm_opportunity_update__display ON doc_crm_opportunity_update(display);
CREATE INDEX IF NOT EXISTS ix_doc_crm_opportunity_update__opportunity_id ON doc_crm_opportunity_update(opportunity_id);
CREATE INDEX IF NOT EXISTS ix_doc_crm_opportunity_update__stage_id ON doc_crm_opportunity_update(stage_id);
CREATE INDEX IF NOT EXISTS ix_doc_crm_opportunity_update__status ON doc_crm_opportunity_update(status);

CREATE TABLE IF NOT EXISTS doc_crm_quote (
    document_id          uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display              text NULL,
    document_date_utc    date NOT NULL,
    opportunity_id       uuid NOT NULL REFERENCES doc_crm_lead_conversion(document_id) ON DELETE RESTRICT,
    account_id           uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    contact_id           uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    valid_until          date NOT NULL,
    currency             text NOT NULL DEFAULT 'USD',
    quote_status         text NOT NULL DEFAULT 'Draft',
    amount               numeric(18, 4) NOT NULL DEFAULT 0,
    notes                text NULL,

    CONSTRAINT ck_doc_crm_quote__valid_until CHECK (valid_until >= document_date_utc),
    CONSTRAINT ck_doc_crm_quote__quote_status CHECK (quote_status IN ('Draft', 'Presented', 'Accepted', 'Rejected', 'Expired')),
    CONSTRAINT ck_doc_crm_quote__amount CHECK (amount >= 0),
    CONSTRAINT ck_doc_crm_quote__currency CHECK (length(btrim(currency)) > 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_crm_quote__display ON doc_crm_quote(display);
CREATE INDEX IF NOT EXISTS ix_doc_crm_quote__opportunity_id ON doc_crm_quote(opportunity_id);
CREATE INDEX IF NOT EXISTS ix_doc_crm_quote__account_id ON doc_crm_quote(account_id);
CREATE INDEX IF NOT EXISTS ix_doc_crm_quote__quote_status ON doc_crm_quote(quote_status);

CREATE TABLE IF NOT EXISTS doc_crm_quote__lines (
    document_id          uuid NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    ordinal              integer NOT NULL,
    product_id           uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    description          text NULL,
    quantity             numeric(18, 4) NOT NULL,
    unit_price           numeric(18, 4) NOT NULL,
    discount_percent     numeric(5, 2) NOT NULL DEFAULT 0,
    line_amount          numeric(18, 4) NOT NULL,

    CONSTRAINT pk_doc_crm_quote__lines PRIMARY KEY (document_id, ordinal),
    CONSTRAINT fk_doc_crm_quote__lines__head
        FOREIGN KEY (document_id) REFERENCES doc_crm_quote(document_id) ON DELETE CASCADE,
    CONSTRAINT ck_doc_crm_quote__lines__ordinal CHECK (ordinal > 0),
    CONSTRAINT ck_doc_crm_quote__lines__quantity CHECK (quantity > 0),
    CONSTRAINT ck_doc_crm_quote__lines__unit_price CHECK (unit_price >= 0),
    CONSTRAINT ck_doc_crm_quote__lines__discount CHECK (discount_percent >= 0 AND discount_percent <= 100),
    CONSTRAINT ck_doc_crm_quote__lines__line_amount CHECK (line_amount >= 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_crm_quote__lines__document_id ON doc_crm_quote__lines(document_id);
CREATE INDEX IF NOT EXISTS ix_doc_crm_quote__lines__product_id ON doc_crm_quote__lines(product_id);

CREATE TABLE IF NOT EXISTS doc_crm_activity_log (
    document_id          uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display              text NULL,
    document_date_utc    date NOT NULL,
    activity_type        text NOT NULL,
    subject              text NOT NULL,
    lead_intake_id       uuid NULL REFERENCES doc_crm_lead_intake(document_id) ON DELETE RESTRICT,
    account_id           uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    contact_id           uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    opportunity_id       uuid NULL REFERENCES doc_crm_lead_conversion(document_id) ON DELETE RESTRICT,
    due_at_utc           timestamptz NULL,
    completed_at_utc     timestamptz NULL,
    outcome              text NULL,
    notes                text NULL,

    CONSTRAINT ck_doc_crm_activity_log__activity_type
        CHECK (activity_type IN ('Call', 'Email', 'Meeting', 'Task', 'Note')),
    CONSTRAINT ck_doc_crm_activity_log__subject CHECK (length(btrim(subject)) > 0),
    CONSTRAINT ck_doc_crm_activity_log__target
        CHECK (lead_intake_id IS NOT NULL OR account_id IS NOT NULL OR contact_id IS NOT NULL OR opportunity_id IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS ix_doc_crm_activity_log__display ON doc_crm_activity_log(display);
CREATE INDEX IF NOT EXISTS ix_doc_crm_activity_log__activity_type ON doc_crm_activity_log(activity_type);
CREATE INDEX IF NOT EXISTS ix_doc_crm_activity_log__lead_intake_id ON doc_crm_activity_log(lead_intake_id);
CREATE INDEX IF NOT EXISTS ix_doc_crm_activity_log__account_id ON doc_crm_activity_log(account_id);
CREATE INDEX IF NOT EXISTS ix_doc_crm_activity_log__contact_id ON doc_crm_activity_log(contact_id);
CREATE INDEX IF NOT EXISTS ix_doc_crm_activity_log__opportunity_id ON doc_crm_activity_log(opportunity_id);

-- -----------------------------------------------------------------------------
-- Typed display and quote amount helpers
-- -----------------------------------------------------------------------------

CREATE OR REPLACE FUNCTION crm_quote_refresh_amount()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE doc_crm_quote q
       SET amount = COALESCE((
           SELECT SUM(l.line_amount)
             FROM doc_crm_quote__lines l
            WHERE l.document_id = COALESCE(NEW.document_id, OLD.document_id)
       ), 0)
     WHERE q.document_id = COALESCE(NEW.document_id, OLD.document_id);
    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_crm_quote__lines_refresh_amount_insert_update ON doc_crm_quote__lines;
CREATE TRIGGER trg_doc_crm_quote__lines_refresh_amount_insert_update
AFTER INSERT OR UPDATE OR DELETE ON doc_crm_quote__lines
FOR EACH ROW
EXECUTE FUNCTION crm_quote_refresh_amount();

CREATE OR REPLACE FUNCTION crm_refresh_document_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    doc_number text;
    doc_date text;
BEGIN
    SELECT NULLIF(BTRIM(d.number), '')
      INTO doc_number
      FROM documents d
     WHERE d.id = NEW.document_id;

    doc_date := to_char(NEW.document_date_utc, 'FMMM/FMDD/YYYY');

    IF TG_TABLE_NAME = 'doc_crm_lead_intake' THEN
        NEW.display := CONCAT_WS(' ', 'Lead Intake', doc_number, doc_date);
    ELSIF TG_TABLE_NAME = 'doc_crm_lead_qualification' THEN
        NEW.display := CONCAT_WS(' ', 'Lead Qualification', doc_number, doc_date);
    ELSIF TG_TABLE_NAME = 'doc_crm_lead_conversion' THEN
        NEW.display := CONCAT_WS(' ', 'Lead Conversion', doc_number, doc_date);
    ELSIF TG_TABLE_NAME = 'doc_crm_opportunity_update' THEN
        NEW.display := CONCAT_WS(' ', 'Opportunity Update', doc_number, doc_date);
    ELSIF TG_TABLE_NAME = 'doc_crm_quote' THEN
        NEW.display := CONCAT_WS(' ', 'Quote', doc_number, doc_date);
    ELSIF TG_TABLE_NAME = 'doc_crm_activity_log' THEN
        NEW.display := CONCAT_WS(' ', 'Activity Log', doc_number, doc_date);
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_crm_lead_intake_refresh_display ON doc_crm_lead_intake;
CREATE TRIGGER trg_doc_crm_lead_intake_refresh_display
BEFORE INSERT OR UPDATE ON doc_crm_lead_intake
FOR EACH ROW EXECUTE FUNCTION crm_refresh_document_display();

DROP TRIGGER IF EXISTS trg_doc_crm_lead_qualification_refresh_display ON doc_crm_lead_qualification;
CREATE TRIGGER trg_doc_crm_lead_qualification_refresh_display
BEFORE INSERT OR UPDATE ON doc_crm_lead_qualification
FOR EACH ROW EXECUTE FUNCTION crm_refresh_document_display();

DROP TRIGGER IF EXISTS trg_doc_crm_lead_conversion_refresh_display ON doc_crm_lead_conversion;
CREATE TRIGGER trg_doc_crm_lead_conversion_refresh_display
BEFORE INSERT OR UPDATE ON doc_crm_lead_conversion
FOR EACH ROW EXECUTE FUNCTION crm_refresh_document_display();

DROP TRIGGER IF EXISTS trg_doc_crm_opportunity_update_refresh_display ON doc_crm_opportunity_update;
CREATE TRIGGER trg_doc_crm_opportunity_update_refresh_display
BEFORE INSERT OR UPDATE ON doc_crm_opportunity_update
FOR EACH ROW EXECUTE FUNCTION crm_refresh_document_display();

DROP TRIGGER IF EXISTS trg_doc_crm_quote_refresh_display ON doc_crm_quote;
CREATE TRIGGER trg_doc_crm_quote_refresh_display
BEFORE INSERT OR UPDATE ON doc_crm_quote
FOR EACH ROW EXECUTE FUNCTION crm_refresh_document_display();

DROP TRIGGER IF EXISTS trg_doc_crm_activity_log_refresh_display ON doc_crm_activity_log;
CREATE TRIGGER trg_doc_crm_activity_log_refresh_display
BEFORE INSERT OR UPDATE ON doc_crm_activity_log
FOR EACH ROW EXECUTE FUNCTION crm_refresh_document_display();

-- -----------------------------------------------------------------------------
-- Read-side projections and reporting views
-- -----------------------------------------------------------------------------

CREATE OR REPLACE VIEW crm_lead_history AS
SELECT
    d.posted_at_utc AS event_at_utc,
    'Intake'::text AS event_type,
    li.document_id AS lead_id,
    li.lead_name,
    li.company_name,
    li.contact_name,
    li.email,
    li.phone,
    li.lead_source,
    li.industry,
    li.estimated_value,
    li.currency,
    NULL::text AS qualification_state,
    NULL::integer AS score,
    NULL::uuid AS conversion_document_id
FROM doc_crm_lead_intake li
JOIN documents d ON d.id = li.document_id AND d.status = 2
UNION ALL
SELECT
    d.posted_at_utc,
    'Qualification'::text,
    lq.lead_intake_id,
    li.lead_name,
    li.company_name,
    li.contact_name,
    li.email,
    li.phone,
    li.lead_source,
    li.industry,
    li.estimated_value,
    li.currency,
    lq.qualification_state,
    lq.score,
    NULL::uuid
FROM doc_crm_lead_qualification lq
JOIN documents d ON d.id = lq.document_id AND d.status = 2
JOIN doc_crm_lead_intake li ON li.document_id = lq.lead_intake_id
UNION ALL
SELECT
    d.posted_at_utc,
    'Conversion'::text,
    lc.lead_intake_id,
    li.lead_name,
    li.company_name,
    li.contact_name,
    li.email,
    li.phone,
    li.lead_source,
    li.industry,
    li.estimated_value,
    li.currency,
    'Converted'::text,
    NULL::integer,
    lc.document_id
FROM doc_crm_lead_conversion lc
JOIN documents d ON d.id = lc.document_id AND d.status = 2
JOIN doc_crm_lead_intake li ON li.document_id = lc.lead_intake_id;

CREATE OR REPLACE VIEW crm_leads_current AS
WITH latest_qualification AS (
    SELECT DISTINCT ON (lq.lead_intake_id)
        lq.*
    FROM doc_crm_lead_qualification lq
    JOIN documents d ON d.id = lq.document_id AND d.status = 2
    ORDER BY lq.lead_intake_id, d.posted_at_utc DESC, lq.document_id DESC
),
latest_conversion AS (
    SELECT DISTINCT ON (lc.lead_intake_id)
        lc.*
    FROM doc_crm_lead_conversion lc
    JOIN documents d ON d.id = lc.document_id AND d.status = 2
    ORDER BY lc.lead_intake_id, d.posted_at_utc DESC, lc.document_id DESC
)
SELECT
    li.document_id AS lead_id,
    li.lead_name,
    li.company_name,
    li.contact_name,
    li.email,
    li.phone,
    li.lead_source,
    li.industry,
    li.estimated_value,
    li.currency,
    COALESCE(lc.document_id IS NOT NULL, false) AS is_converted,
    COALESCE(lq.qualification_state, 'New') AS qualification_state,
    lq.score,
    lc.document_id AS conversion_document_id,
    lc.account_id,
    lc.contact_id
FROM doc_crm_lead_intake li
JOIN documents d ON d.id = li.document_id AND d.status = 2
LEFT JOIN latest_qualification lq ON lq.lead_intake_id = li.document_id
LEFT JOIN latest_conversion lc ON lc.lead_intake_id = li.document_id;

CREATE OR REPLACE VIEW crm_opportunity_history AS
SELECT
    d.posted_at_utc AS event_at_utc,
    'Conversion'::text AS event_type,
    lc.document_id AS opportunity_id,
    lc.opportunity_name,
    lc.account_id,
    a.display AS account_display,
    lc.stage_id,
    s.display AS stage_display,
    COALESCE(lc.amount, 0) AS amount,
    COALESCE(lc.probability, s.default_probability, 0) AS probability,
    lc.expected_close_date,
    'Open'::text AS status
FROM doc_crm_lead_conversion lc
JOIN documents d ON d.id = lc.document_id AND d.status = 2
LEFT JOIN cat_crm_account a ON a.catalog_id = lc.account_id
LEFT JOIN cat_crm_opportunity_stage s ON s.catalog_id = lc.stage_id
WHERE lc.create_opportunity
UNION ALL
SELECT
    d.posted_at_utc,
    'Update'::text,
    ou.opportunity_id,
    lc.opportunity_name,
    lc.account_id,
    a.display,
    ou.stage_id,
    s.display,
    ou.amount,
    ou.probability,
    ou.expected_close_date,
    ou.status
FROM doc_crm_opportunity_update ou
JOIN documents d ON d.id = ou.document_id AND d.status = 2
JOIN doc_crm_lead_conversion lc ON lc.document_id = ou.opportunity_id
LEFT JOIN cat_crm_account a ON a.catalog_id = lc.account_id
LEFT JOIN cat_crm_opportunity_stage s ON s.catalog_id = ou.stage_id;

CREATE OR REPLACE VIEW crm_opportunities_current AS
WITH latest_update AS (
    SELECT DISTINCT ON (ou.opportunity_id)
        ou.*
    FROM doc_crm_opportunity_update ou
    JOIN documents d ON d.id = ou.document_id AND d.status = 2
    ORDER BY ou.opportunity_id, d.posted_at_utc DESC, ou.document_id DESC
)
SELECT
    lc.document_id AS opportunity_id,
    lc.opportunity_name,
    lc.account_id,
    a.display AS account_display,
    COALESCE(u.stage_id, lc.stage_id) AS stage_id,
    s.display AS stage_display,
    COALESCE(u.amount, lc.amount, 0) AS amount,
    COALESCE(u.probability, lc.probability, s.default_probability, 0) AS probability,
    ROUND(COALESCE(u.amount, lc.amount, 0) * COALESCE(u.probability, lc.probability, s.default_probability, 0) / 100.0, 4) AS weighted_amount,
    COALESCE(u.expected_close_date, lc.expected_close_date) AS expected_close_date,
    COALESCE(u.status, 'Open') AS status,
    lc.currency
FROM doc_crm_lead_conversion lc
JOIN documents d ON d.id = lc.document_id AND d.status = 2
LEFT JOIN latest_update u ON u.opportunity_id = lc.document_id
LEFT JOIN cat_crm_account a ON a.catalog_id = lc.account_id
LEFT JOIN cat_crm_opportunity_stage s ON s.catalog_id = COALESCE(u.stage_id, lc.stage_id)
WHERE lc.create_opportunity;

CREATE OR REPLACE VIEW crm_quotes_current AS
SELECT
    q.document_id AS quote_id,
    q.document_date_utc AS quote_date,
    q.opportunity_id,
    q.account_id,
    a.display AS account_display,
    q.contact_id,
    c.display AS contact_display,
    q.valid_until,
    q.currency,
    q.quote_status,
    q.amount,
    1::bigint AS quote_count
FROM doc_crm_quote q
JOIN documents d ON d.id = q.document_id AND d.status = 2
LEFT JOIN cat_crm_account a ON a.catalog_id = q.account_id
LEFT JOIN cat_crm_contact c ON c.catalog_id = q.contact_id;

CREATE OR REPLACE VIEW crm_quote_lines_current AS
SELECT
    q.document_id AS quote_id,
    l.ordinal,
    l.product_id,
    p.display AS product_display,
    l.description,
    l.quantity,
    l.unit_price,
    l.discount_percent,
    l.line_amount
FROM doc_crm_quote q
JOIN documents d ON d.id = q.document_id AND d.status = 2
JOIN doc_crm_quote__lines l ON l.document_id = q.document_id
LEFT JOIN cat_crm_product p ON p.catalog_id = l.product_id;

CREATE OR REPLACE VIEW crm_activities_current AS
SELECT
    al.document_id AS activity_id,
    al.document_date_utc AS activity_date,
    al.activity_type,
    al.subject,
    al.lead_intake_id,
    al.account_id,
    al.contact_id,
    al.opportunity_id,
    al.due_at_utc,
    al.completed_at_utc,
    al.outcome,
    1::bigint AS activity_count
FROM doc_crm_activity_log al
JOIN documents d ON d.id = al.document_id AND d.status = 2;

CREATE OR REPLACE VIEW crm_lead_funnel AS
SELECT
    '01 Intake'::text AS funnel_step,
    lead_source,
    industry,
    COUNT(*)::bigint AS lead_count
FROM crm_leads_current
GROUP BY lead_source, industry
UNION ALL
SELECT
    '02 Qualified'::text,
    lead_source,
    industry,
    COUNT(*)::bigint
FROM crm_leads_current
WHERE qualification_state = 'Qualified'
GROUP BY lead_source, industry
UNION ALL
SELECT
    '03 Converted'::text,
    lead_source,
    industry,
    COUNT(*)::bigint
FROM crm_leads_current
WHERE is_converted
GROUP BY lead_source, industry;

SELECT ngb_install_mirrored_document_relationship_trigger('doc_crm_lead_qualification', 'lead_intake_id', 'qualifies');
SELECT ngb_install_mirrored_document_relationship_trigger('doc_crm_lead_qualification', 'lead_intake_id', 'created_from');
SELECT ngb_install_mirrored_document_relationship_trigger('doc_crm_lead_conversion', 'lead_intake_id', 'converts');
SELECT ngb_install_mirrored_document_relationship_trigger('doc_crm_lead_conversion', 'lead_intake_id', 'created_from');
SELECT ngb_install_mirrored_document_relationship_trigger('doc_crm_opportunity_update', 'opportunity_id', 'updates');
SELECT ngb_install_mirrored_document_relationship_trigger('doc_crm_opportunity_update', 'opportunity_id', 'created_from');
SELECT ngb_install_mirrored_document_relationship_trigger('doc_crm_quote', 'opportunity_id', 'quotes');
SELECT ngb_install_mirrored_document_relationship_trigger('doc_crm_quote', 'opportunity_id', 'created_from');
SELECT ngb_install_mirrored_document_relationship_trigger('doc_crm_activity_log', 'lead_intake_id', 'activity_for_lead');
SELECT ngb_install_mirrored_document_relationship_trigger('doc_crm_activity_log', 'lead_intake_id', 'related_to');
SELECT ngb_install_mirrored_document_relationship_trigger('doc_crm_activity_log', 'opportunity_id', 'activity_for_opportunity');
SELECT ngb_install_mirrored_document_relationship_trigger('doc_crm_activity_log', 'opportunity_id', 'related_to');

SELECT ngb_install_typed_document_immutability_guards();
