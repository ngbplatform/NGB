-- NGB.AgencyBilling clean baseline for recreated databases.
--
-- Scope:
-- - final Agency Billing typed schema for phase-1 master data + document slice
-- - no business seed data (use migrator command: seed-defaults)

SET TIME ZONE 'UTC';
SET search_path = public;

-- -----------------------------------------------------------------------------
-- Catalogs
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS cat_ab_payment_terms (
    catalog_id   uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display      text NOT NULL,
    code         text NOT NULL,
    name         text NOT NULL,
    due_days     integer NOT NULL,
    is_active    boolean NOT NULL,

    CONSTRAINT ck_cat_ab_payment_terms__code
        CHECK (btrim(code) <> ''),
    CONSTRAINT ck_cat_ab_payment_terms__due_days
        CHECK (due_days >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_ab_payment_terms__code
    ON cat_ab_payment_terms(code);

CREATE INDEX IF NOT EXISTS ix_cat_ab_payment_terms__display
    ON cat_ab_payment_terms(display);

CREATE INDEX IF NOT EXISTS ix_cat_ab_payment_terms__name
    ON cat_ab_payment_terms(name);

CREATE INDEX IF NOT EXISTS ix_cat_ab_payment_terms__due_days
    ON cat_ab_payment_terms(due_days);

CREATE INDEX IF NOT EXISTS ix_cat_ab_payment_terms__is_active
    ON cat_ab_payment_terms(is_active);

CREATE TABLE IF NOT EXISTS cat_ab_client (
    catalog_id         uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display            text NOT NULL,
    client_code        text NULL,
    name               text NOT NULL,
    legal_name         text NULL,
    status             integer NOT NULL,
    email              text NULL,
    phone              text NULL,
    billing_contact    text NULL,
    payment_terms_id   uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    default_currency   text NULL,
    is_active          boolean NOT NULL,
    notes              text NULL,
    CONSTRAINT ck_cat_ab_client__default_currency
        CHECK (default_currency IS NULL OR btrim(default_currency) <> '')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_ab_client__client_code
    ON cat_ab_client(client_code)
    WHERE client_code IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_cat_ab_client__display
    ON cat_ab_client(display);

CREATE INDEX IF NOT EXISTS ix_cat_ab_client__name
    ON cat_ab_client(name);

CREATE INDEX IF NOT EXISTS ix_cat_ab_client__status
    ON cat_ab_client(status);

CREATE INDEX IF NOT EXISTS ix_cat_ab_client__payment_terms_id
    ON cat_ab_client(payment_terms_id);

CREATE INDEX IF NOT EXISTS ix_cat_ab_client__is_active
    ON cat_ab_client(is_active);

CREATE TABLE IF NOT EXISTS cat_ab_team_member (
    catalog_id             uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display                text NOT NULL,
    member_code            text NULL,
    full_name              text NOT NULL,
    member_type            integer NOT NULL,
    is_active              boolean NOT NULL,
    billable_by_default    boolean NOT NULL,
    default_billing_rate   numeric(18, 4) NULL,
    default_cost_rate      numeric(18, 4) NULL,
    email                  text NULL,
    title                  text NULL,

    CONSTRAINT ck_cat_ab_team_member__default_billing_rate
        CHECK (default_billing_rate IS NULL OR default_billing_rate >= 0),
    CONSTRAINT ck_cat_ab_team_member__default_cost_rate
        CHECK (default_cost_rate IS NULL OR default_cost_rate >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_ab_team_member__member_code
    ON cat_ab_team_member(member_code)
    WHERE member_code IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_cat_ab_team_member__display
    ON cat_ab_team_member(display);

CREATE INDEX IF NOT EXISTS ix_cat_ab_team_member__full_name
    ON cat_ab_team_member(full_name);

CREATE INDEX IF NOT EXISTS ix_cat_ab_team_member__member_type
    ON cat_ab_team_member(member_type);

CREATE INDEX IF NOT EXISTS ix_cat_ab_team_member__is_active
    ON cat_ab_team_member(is_active);

CREATE TABLE IF NOT EXISTS cat_ab_project (
    catalog_id           uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display              text NOT NULL,
    project_code         text NULL,
    name                 text NOT NULL,
    client_id            uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    project_manager_id   uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    start_date           date NULL,
    end_date             date NULL,
    status               integer NOT NULL,
    billing_model        integer NOT NULL,
    budget_hours         numeric(18, 4) NULL,
    budget_amount        numeric(18, 4) NULL,
    notes                text NULL,

    CONSTRAINT ck_cat_ab_project__budget_hours
        CHECK (budget_hours IS NULL OR budget_hours >= 0),
    CONSTRAINT ck_cat_ab_project__budget_amount
        CHECK (budget_amount IS NULL OR budget_amount >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_ab_project__project_code
    ON cat_ab_project(project_code)
    WHERE project_code IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_cat_ab_project__display
    ON cat_ab_project(display);

CREATE INDEX IF NOT EXISTS ix_cat_ab_project__client_id
    ON cat_ab_project(client_id);

CREATE INDEX IF NOT EXISTS ix_cat_ab_project__project_manager_id
    ON cat_ab_project(project_manager_id);

CREATE INDEX IF NOT EXISTS ix_cat_ab_project__status
    ON cat_ab_project(status);

CREATE INDEX IF NOT EXISTS ix_cat_ab_project__billing_model
    ON cat_ab_project(billing_model);

CREATE TABLE IF NOT EXISTS cat_ab_service_item (
    catalog_id                    uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display                       text NOT NULL,
    code                          text NOT NULL,
    name                          text NOT NULL,
    unit_of_measure               integer NULL,
    default_revenue_account_id    uuid NULL REFERENCES accounting_accounts(account_id),
    is_active                     boolean NOT NULL,
    notes                         text NULL,

    CONSTRAINT ck_cat_ab_service_item__code
        CHECK (btrim(code) <> '')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_ab_service_item__code
    ON cat_ab_service_item(code);

CREATE INDEX IF NOT EXISTS ix_cat_ab_service_item__display
    ON cat_ab_service_item(display);

CREATE INDEX IF NOT EXISTS ix_cat_ab_service_item__name
    ON cat_ab_service_item(name);

CREATE INDEX IF NOT EXISTS ix_cat_ab_service_item__default_revenue_account_id
    ON cat_ab_service_item(default_revenue_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_ab_service_item__is_active
    ON cat_ab_service_item(is_active);

CREATE TABLE IF NOT EXISTS cat_ab_rate_card (
    catalog_id       uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display          text NOT NULL,
    name             text NOT NULL,
    client_id        uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    project_id       uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    team_member_id   uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    service_item_id  uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    service_title    text NULL,
    billing_rate     numeric(18, 4) NOT NULL,
    cost_rate        numeric(18, 4) NULL,
    effective_from   date NULL,
    effective_to     date NULL,
    is_active        boolean NOT NULL,
    notes            text NULL,

    CONSTRAINT ck_cat_ab_rate_card__billing_rate
        CHECK (billing_rate >= 0),
    CONSTRAINT ck_cat_ab_rate_card__cost_rate
        CHECK (cost_rate IS NULL OR cost_rate >= 0)
);

CREATE INDEX IF NOT EXISTS ix_cat_ab_rate_card__display
    ON cat_ab_rate_card(display);

CREATE INDEX IF NOT EXISTS ix_cat_ab_rate_card__name
    ON cat_ab_rate_card(name);

CREATE INDEX IF NOT EXISTS ix_cat_ab_rate_card__client_id
    ON cat_ab_rate_card(client_id);

CREATE INDEX IF NOT EXISTS ix_cat_ab_rate_card__project_id
    ON cat_ab_rate_card(project_id);

CREATE INDEX IF NOT EXISTS ix_cat_ab_rate_card__team_member_id
    ON cat_ab_rate_card(team_member_id);

CREATE INDEX IF NOT EXISTS ix_cat_ab_rate_card__service_item_id
    ON cat_ab_rate_card(service_item_id);

CREATE INDEX IF NOT EXISTS ix_cat_ab_rate_card__is_active
    ON cat_ab_rate_card(is_active);

CREATE TABLE IF NOT EXISTS cat_ab_accounting_policy (
    catalog_id                           uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display                              text NOT NULL,
    cash_account_id                      uuid NULL REFERENCES accounting_accounts(account_id),
    ar_account_id                        uuid NULL REFERENCES accounting_accounts(account_id),
    service_revenue_account_id           uuid NULL REFERENCES accounting_accounts(account_id),
    project_time_ledger_register_id      uuid NULL REFERENCES operational_registers(register_id),
    unbilled_time_register_id            uuid NULL REFERENCES operational_registers(register_id),
    project_billing_status_register_id   uuid NULL REFERENCES operational_registers(register_id),
    ar_open_items_register_id            uuid NULL REFERENCES operational_registers(register_id),
    default_currency                     text NULL,

    CONSTRAINT ck_cat_ab_accounting_policy__default_currency
        CHECK (default_currency IS NULL OR btrim(default_currency) <> '')
);

CREATE INDEX IF NOT EXISTS ix_cat_ab_accounting_policy__display
    ON cat_ab_accounting_policy(display);

CREATE INDEX IF NOT EXISTS ix_cat_ab_accounting_policy__cash_account_id
    ON cat_ab_accounting_policy(cash_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_ab_accounting_policy__ar_account_id
    ON cat_ab_accounting_policy(ar_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_ab_accounting_policy__service_revenue_account_id
    ON cat_ab_accounting_policy(service_revenue_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_ab_accounting_policy__ar_open_items_register_id
    ON cat_ab_accounting_policy(ar_open_items_register_id);

-- -----------------------------------------------------------------------------
-- Documents
-- -----------------------------------------------------------------------------

CREATE OR REPLACE FUNCTION ngb_ab_build_document_display(
    p_title text,
    p_document_id uuid)
RETURNS text
LANGUAGE plpgsql
AS $$
DECLARE
    v_number text;
    v_date   date;
BEGIN
    SELECT NULLIF(BTRIM(d.number), ''),
           ((d.date_utc AT TIME ZONE 'UTC')::date)
      INTO v_number, v_date
      FROM documents d
     WHERE d.id = p_document_id;

    RETURN CONCAT_WS(
        ' ',
        NULLIF(BTRIM(p_title), ''),
        v_number,
        CASE WHEN v_date IS NULL THEN NULL ELSE TO_CHAR(v_date, 'FMMM/FMDD/YYYY') END);
END;
$$;

CREATE TABLE IF NOT EXISTS doc_ab_client_contract (
    document_id             uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display                 text NOT NULL,
    effective_from          date NOT NULL,
    effective_to            date NULL,
    client_id               uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    project_id              uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    currency_code           text NOT NULL,
    billing_frequency       integer NOT NULL,
    payment_terms_id        uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    invoice_memo_template   text NULL,
    is_active               boolean NOT NULL,
    notes                   text NULL,

    CONSTRAINT ck_doc_ab_client_contract__currency_code
        CHECK (btrim(currency_code) <> '')
);

CREATE INDEX IF NOT EXISTS ix_doc_ab_client_contract__display
    ON doc_ab_client_contract(display);

CREATE INDEX IF NOT EXISTS ix_doc_ab_client_contract__effective_from
    ON doc_ab_client_contract(effective_from);

CREATE INDEX IF NOT EXISTS ix_doc_ab_client_contract__client_id
    ON doc_ab_client_contract(client_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_client_contract__project_id
    ON doc_ab_client_contract(project_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_client_contract__payment_terms_id
    ON doc_ab_client_contract(payment_terms_id);

CREATE TABLE IF NOT EXISTS doc_ab_client_contract__lines (
    document_id      uuid NOT NULL,
    ordinal          integer NOT NULL,
    service_item_id  uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    team_member_id   uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    service_title    text NULL,
    billing_rate     numeric(18, 4) NOT NULL,
    cost_rate        numeric(18, 4) NULL,
    active_from      date NULL,
    active_to        date NULL,
    notes            text NULL,

    CONSTRAINT fk_doc_ab_client_contract__lines__document
        FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
    CONSTRAINT fk_doc_ab_client_contract__lines__head
        FOREIGN KEY (document_id) REFERENCES doc_ab_client_contract(document_id) ON DELETE CASCADE,
    CONSTRAINT ck_doc_ab_client_contract__lines__ordinal
        CHECK (ordinal > 0),
    CONSTRAINT ck_doc_ab_client_contract__lines__billing_rate
        CHECK (billing_rate >= 0),
    CONSTRAINT ck_doc_ab_client_contract__lines__cost_rate
        CHECK (cost_rate IS NULL OR cost_rate >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_ab_client_contract__lines__document_ordinal
    ON doc_ab_client_contract__lines(document_id, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_ab_client_contract__lines__document_id
    ON doc_ab_client_contract__lines(document_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_client_contract__lines__service_item_id
    ON doc_ab_client_contract__lines(service_item_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_client_contract__lines__team_member_id
    ON doc_ab_client_contract__lines(team_member_id);

CREATE TABLE IF NOT EXISTS doc_ab_timesheet (
    document_id         uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display             text NOT NULL,
    document_date_utc   date NOT NULL,
    team_member_id      uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    project_id          uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    client_id           uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    work_date           date NOT NULL,
    total_hours         numeric(18, 4) NOT NULL DEFAULT 0,
    amount              numeric(18, 4) NOT NULL DEFAULT 0,
    cost_amount         numeric(18, 4) NOT NULL DEFAULT 0,
    notes               text NULL,

    CONSTRAINT ck_doc_ab_timesheet__total_hours
        CHECK (total_hours >= 0),
    CONSTRAINT ck_doc_ab_timesheet__amount
        CHECK (amount >= 0),
    CONSTRAINT ck_doc_ab_timesheet__cost_amount
        CHECK (cost_amount >= 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_ab_timesheet__display
    ON doc_ab_timesheet(display);

CREATE INDEX IF NOT EXISTS ix_doc_ab_timesheet__document_date_utc
    ON doc_ab_timesheet(document_date_utc);

CREATE INDEX IF NOT EXISTS ix_doc_ab_timesheet__team_member_id
    ON doc_ab_timesheet(team_member_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_timesheet__project_id
    ON doc_ab_timesheet(project_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_timesheet__client_id
    ON doc_ab_timesheet(client_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_timesheet__work_date
    ON doc_ab_timesheet(work_date);

CREATE TABLE IF NOT EXISTS doc_ab_timesheet__lines (
    document_id        uuid NOT NULL,
    ordinal            integer NOT NULL,
    service_item_id    uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    description        text NULL,
    hours              numeric(18, 4) NOT NULL,
    billable           boolean NOT NULL,
    billing_rate       numeric(18, 4) NULL,
    cost_rate          numeric(18, 4) NULL,
    line_amount        numeric(18, 4) NULL,
    line_cost_amount   numeric(18, 4) NULL,

    CONSTRAINT fk_doc_ab_timesheet__lines__document
        FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
    CONSTRAINT fk_doc_ab_timesheet__lines__head
        FOREIGN KEY (document_id) REFERENCES doc_ab_timesheet(document_id) ON DELETE CASCADE,
    CONSTRAINT ck_doc_ab_timesheet__lines__ordinal
        CHECK (ordinal > 0),
    CONSTRAINT ck_doc_ab_timesheet__lines__hours
        CHECK (hours > 0),
    CONSTRAINT ck_doc_ab_timesheet__lines__billing_rate
        CHECK (billing_rate IS NULL OR billing_rate >= 0),
    CONSTRAINT ck_doc_ab_timesheet__lines__cost_rate
        CHECK (cost_rate IS NULL OR cost_rate >= 0),
    CONSTRAINT ck_doc_ab_timesheet__lines__line_amount
        CHECK (line_amount IS NULL OR line_amount >= 0),
    CONSTRAINT ck_doc_ab_timesheet__lines__line_cost_amount
        CHECK (line_cost_amount IS NULL OR line_cost_amount >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_ab_timesheet__lines__document_ordinal
    ON doc_ab_timesheet__lines(document_id, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_ab_timesheet__lines__document_id
    ON doc_ab_timesheet__lines(document_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_timesheet__lines__service_item_id
    ON doc_ab_timesheet__lines(service_item_id);

CREATE TABLE IF NOT EXISTS doc_ab_sales_invoice (
    document_id         uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display             text NOT NULL,
    document_date_utc   date NOT NULL,
    due_date            date NOT NULL,
    client_id           uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    project_id          uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    contract_id         uuid NULL REFERENCES doc_ab_client_contract(document_id) ON DELETE RESTRICT,
    currency_code       text NOT NULL,
    memo                text NULL,
    amount              numeric(18, 4) NOT NULL DEFAULT 0,
    notes               text NULL,

    CONSTRAINT ck_doc_ab_sales_invoice__currency_code
        CHECK (btrim(currency_code) <> ''),
    CONSTRAINT ck_doc_ab_sales_invoice__amount
        CHECK (amount >= 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_ab_sales_invoice__display
    ON doc_ab_sales_invoice(display);

CREATE INDEX IF NOT EXISTS ix_doc_ab_sales_invoice__document_date_utc
    ON doc_ab_sales_invoice(document_date_utc);

CREATE INDEX IF NOT EXISTS ix_doc_ab_sales_invoice__due_date
    ON doc_ab_sales_invoice(due_date);

CREATE INDEX IF NOT EXISTS ix_doc_ab_sales_invoice__client_id
    ON doc_ab_sales_invoice(client_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_sales_invoice__project_id
    ON doc_ab_sales_invoice(project_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_sales_invoice__contract_id
    ON doc_ab_sales_invoice(contract_id);

CREATE TABLE IF NOT EXISTS doc_ab_sales_invoice__lines (
    document_id         uuid NOT NULL,
    ordinal             integer NOT NULL,
    service_item_id     uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    source_timesheet_id uuid NULL REFERENCES doc_ab_timesheet(document_id) ON DELETE RESTRICT,
    description         text NOT NULL,
    quantity_hours      numeric(18, 4) NOT NULL,
    rate                numeric(18, 4) NOT NULL,
    line_amount         numeric(18, 4) NOT NULL,

    CONSTRAINT fk_doc_ab_sales_invoice__lines__document
        FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
    CONSTRAINT fk_doc_ab_sales_invoice__lines__head
        FOREIGN KEY (document_id) REFERENCES doc_ab_sales_invoice(document_id) ON DELETE CASCADE,
    CONSTRAINT ck_doc_ab_sales_invoice__lines__ordinal
        CHECK (ordinal > 0),
    CONSTRAINT ck_doc_ab_sales_invoice__lines__description
        CHECK (btrim(description) <> ''),
    CONSTRAINT ck_doc_ab_sales_invoice__lines__quantity_hours
        CHECK (quantity_hours > 0),
    CONSTRAINT ck_doc_ab_sales_invoice__lines__rate
        CHECK (rate >= 0),
    CONSTRAINT ck_doc_ab_sales_invoice__lines__line_amount
        CHECK (line_amount >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_ab_sales_invoice__lines__document_ordinal
    ON doc_ab_sales_invoice__lines(document_id, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_ab_sales_invoice__lines__document_id
    ON doc_ab_sales_invoice__lines(document_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_sales_invoice__lines__service_item_id
    ON doc_ab_sales_invoice__lines(service_item_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_sales_invoice__lines__source_timesheet_id
    ON doc_ab_sales_invoice__lines(source_timesheet_id);

CREATE TABLE IF NOT EXISTS doc_ab_customer_payment (
    document_id         uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display             text NOT NULL,
    document_date_utc   date NOT NULL,
    client_id           uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    cash_account_id     uuid NULL REFERENCES accounting_accounts(account_id),
    reference_number    text NULL,
    amount              numeric(18, 4) NOT NULL,
    notes               text NULL,

    CONSTRAINT ck_doc_ab_customer_payment__amount
        CHECK (amount > 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_ab_customer_payment__display
    ON doc_ab_customer_payment(display);

CREATE INDEX IF NOT EXISTS ix_doc_ab_customer_payment__document_date_utc
    ON doc_ab_customer_payment(document_date_utc);

CREATE INDEX IF NOT EXISTS ix_doc_ab_customer_payment__client_id
    ON doc_ab_customer_payment(client_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_customer_payment__cash_account_id
    ON doc_ab_customer_payment(cash_account_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_customer_payment__reference_number
    ON doc_ab_customer_payment(reference_number);

CREATE TABLE IF NOT EXISTS doc_ab_customer_payment__applies (
    document_id      uuid NOT NULL,
    ordinal          integer NOT NULL,
    sales_invoice_id uuid NOT NULL REFERENCES doc_ab_sales_invoice(document_id) ON DELETE RESTRICT,
    applied_amount   numeric(18, 4) NOT NULL,

    CONSTRAINT fk_doc_ab_customer_payment__applies__document
        FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
    CONSTRAINT fk_doc_ab_customer_payment__applies__head
        FOREIGN KEY (document_id) REFERENCES doc_ab_customer_payment(document_id) ON DELETE CASCADE,
    CONSTRAINT ck_doc_ab_customer_payment__applies__ordinal
        CHECK (ordinal > 0),
    CONSTRAINT ck_doc_ab_customer_payment__applies__applied_amount
        CHECK (applied_amount > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_ab_customer_payment__applies__document_ordinal
    ON doc_ab_customer_payment__applies(document_id, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_ab_customer_payment__applies__document_id
    ON doc_ab_customer_payment__applies(document_id);

CREATE INDEX IF NOT EXISTS ix_doc_ab_customer_payment__applies__sales_invoice_id
    ON doc_ab_customer_payment__applies(sales_invoice_id);

SELECT ngb_install_mirrored_document_relationship_trigger('doc_ab_sales_invoice', 'contract_id', 'based_on');
SELECT ngb_install_mirrored_document_relationship_trigger('doc_ab_sales_invoice__lines', 'source_timesheet_id', 'based_on');
SELECT ngb_install_mirrored_document_relationship_trigger('doc_ab_customer_payment__applies', 'sales_invoice_id', 'based_on');

CREATE OR REPLACE FUNCTION ngb_ab_compute_client_contract_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_ab_build_document_display('Client Contract', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_ab_client_contract__compute_display
    ON doc_ab_client_contract;

CREATE TRIGGER trg_doc_ab_client_contract__compute_display
BEFORE INSERT OR UPDATE ON doc_ab_client_contract
FOR EACH ROW
EXECUTE FUNCTION ngb_ab_compute_client_contract_display();

CREATE OR REPLACE FUNCTION ngb_ab_compute_timesheet_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_ab_build_document_display('Timesheet', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_ab_timesheet__compute_display
    ON doc_ab_timesheet;

CREATE TRIGGER trg_doc_ab_timesheet__compute_display
BEFORE INSERT OR UPDATE ON doc_ab_timesheet
FOR EACH ROW
EXECUTE FUNCTION ngb_ab_compute_timesheet_display();

CREATE OR REPLACE FUNCTION ngb_ab_compute_sales_invoice_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_ab_build_document_display('Sales Invoice', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_ab_sales_invoice__compute_display
    ON doc_ab_sales_invoice;

CREATE TRIGGER trg_doc_ab_sales_invoice__compute_display
BEFORE INSERT OR UPDATE ON doc_ab_sales_invoice
FOR EACH ROW
EXECUTE FUNCTION ngb_ab_compute_sales_invoice_display();

CREATE OR REPLACE FUNCTION ngb_ab_compute_customer_payment_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_ab_build_document_display('Customer Payment', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_ab_customer_payment__compute_display
    ON doc_ab_customer_payment;

CREATE TRIGGER trg_doc_ab_customer_payment__compute_display
BEFORE INSERT OR UPDATE ON doc_ab_customer_payment
FOR EACH ROW
EXECUTE FUNCTION ngb_ab_compute_customer_payment_display();

CREATE OR REPLACE FUNCTION ngb_ab_refresh_document_display_from_header()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    CASE NEW.type_code
        WHEN 'ab.client_contract' THEN
            UPDATE doc_ab_client_contract
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'ab.timesheet' THEN
            UPDATE doc_ab_timesheet
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'ab.sales_invoice' THEN
            UPDATE doc_ab_sales_invoice
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'ab.customer_payment' THEN
            UPDATE doc_ab_customer_payment
               SET display = display
             WHERE document_id = NEW.id;
    END CASE;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_documents__ab_refresh_typed_display ON documents;

CREATE TRIGGER trg_documents__ab_refresh_typed_display
AFTER UPDATE OF number, date_utc ON documents
FOR EACH ROW
WHEN (NEW.type_code IN ('ab.client_contract', 'ab.timesheet', 'ab.sales_invoice', 'ab.customer_payment'))
EXECUTE FUNCTION ngb_ab_refresh_document_display_from_header();

SELECT ngb_install_typed_document_immutability_guards();
