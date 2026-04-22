-- NGB.Trade clean baseline for recreated databases.
--
-- Scope:
-- - final Trade typed schema for master data + pricing slice
-- - no business seed data (use migrator command: seed-defaults)

SET TIME ZONE 'UTC';
SET search_path = public;

-- -----------------------------------------------------------------------------
-- Catalogs
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS cat_trd_party (
    catalog_id         uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display            text NOT NULL,
    party_number       text NULL,
    name               text NOT NULL,
    legal_name         text NULL,
    email              text NULL,
    phone              text NULL,
    tax_id             text NULL,
    billing_address    text NULL,
    shipping_address   text NULL,
    payment_terms_id   uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    default_currency   text NULL,
    is_customer        boolean NOT NULL,
    is_vendor          boolean NOT NULL,
    is_active          boolean NOT NULL,
    notes              text NULL,

    CONSTRAINT ck_cat_trd_party__role
        CHECK (is_customer OR is_vendor),
    CONSTRAINT ck_cat_trd_party__default_currency
        CHECK (default_currency IS NULL OR btrim(default_currency) <> '')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_trd_party__party_number
    ON cat_trd_party(party_number)
    WHERE party_number IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_cat_trd_party__display
    ON cat_trd_party(display);

CREATE INDEX IF NOT EXISTS ix_cat_trd_party__name
    ON cat_trd_party(name);

CREATE INDEX IF NOT EXISTS ix_cat_trd_party__payment_terms_id
    ON cat_trd_party(payment_terms_id);

CREATE INDEX IF NOT EXISTS ix_cat_trd_party__is_customer
    ON cat_trd_party(is_customer);

CREATE INDEX IF NOT EXISTS ix_cat_trd_party__is_vendor
    ON cat_trd_party(is_vendor);

CREATE INDEX IF NOT EXISTS ix_cat_trd_party__is_active
    ON cat_trd_party(is_active);

CREATE TABLE IF NOT EXISTS cat_trd_unit_of_measure (
    catalog_id   uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display      text NOT NULL,
    code         text NOT NULL,
    name         text NOT NULL,
    symbol       text NULL,
    is_active    boolean NOT NULL,

    CONSTRAINT ck_cat_trd_unit_of_measure__code
        CHECK (btrim(code) <> '')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_trd_unit_of_measure__code
    ON cat_trd_unit_of_measure(code);

CREATE INDEX IF NOT EXISTS ix_cat_trd_unit_of_measure__display
    ON cat_trd_unit_of_measure(display);

CREATE INDEX IF NOT EXISTS ix_cat_trd_unit_of_measure__name
    ON cat_trd_unit_of_measure(name);

CREATE INDEX IF NOT EXISTS ix_cat_trd_unit_of_measure__is_active
    ON cat_trd_unit_of_measure(is_active);

CREATE TABLE IF NOT EXISTS cat_trd_price_type (
    catalog_id   uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display      text NOT NULL,
    code         text NOT NULL,
    name         text NOT NULL,
    currency     text NOT NULL,
    is_default   boolean NOT NULL,
    is_active    boolean NOT NULL,
    notes        text NULL,

    CONSTRAINT ck_cat_trd_price_type__code
        CHECK (btrim(code) <> ''),
    CONSTRAINT ck_cat_trd_price_type__currency
        CHECK (btrim(currency) <> '')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_trd_price_type__code
    ON cat_trd_price_type(code);

CREATE INDEX IF NOT EXISTS ix_cat_trd_price_type__display
    ON cat_trd_price_type(display);

CREATE INDEX IF NOT EXISTS ix_cat_trd_price_type__name
    ON cat_trd_price_type(name);

CREATE INDEX IF NOT EXISTS ix_cat_trd_price_type__currency
    ON cat_trd_price_type(currency);

CREATE INDEX IF NOT EXISTS ix_cat_trd_price_type__is_default
    ON cat_trd_price_type(is_default);

CREATE INDEX IF NOT EXISTS ix_cat_trd_price_type__is_active
    ON cat_trd_price_type(is_active);

CREATE TABLE IF NOT EXISTS cat_trd_item (
    catalog_id                     uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display                        text NOT NULL,
    name                           text NOT NULL,
    sku                            text NULL,
    unit_of_measure_id             uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    item_type                      text NULL,
    is_inventory_item              boolean NOT NULL,
    default_sales_price_type_id    uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    is_active                      boolean NOT NULL,
    notes                          text NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_trd_item__sku
    ON cat_trd_item(sku)
    WHERE sku IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_cat_trd_item__display
    ON cat_trd_item(display);

CREATE INDEX IF NOT EXISTS ix_cat_trd_item__unit_of_measure_id
    ON cat_trd_item(unit_of_measure_id);

CREATE INDEX IF NOT EXISTS ix_cat_trd_item__default_sales_price_type_id
    ON cat_trd_item(default_sales_price_type_id);

CREATE INDEX IF NOT EXISTS ix_cat_trd_item__is_inventory_item
    ON cat_trd_item(is_inventory_item);

CREATE INDEX IF NOT EXISTS ix_cat_trd_item__is_active
    ON cat_trd_item(is_active);

CREATE TABLE IF NOT EXISTS cat_trd_warehouse (
    catalog_id        uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display           text NOT NULL,
    warehouse_code    text NULL,
    name              text NOT NULL,
    address           text NULL,
    is_active         boolean NOT NULL,
    notes             text NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_trd_warehouse__warehouse_code
    ON cat_trd_warehouse(warehouse_code)
    WHERE warehouse_code IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_cat_trd_warehouse__display
    ON cat_trd_warehouse(display);

CREATE INDEX IF NOT EXISTS ix_cat_trd_warehouse__name
    ON cat_trd_warehouse(name);

CREATE INDEX IF NOT EXISTS ix_cat_trd_warehouse__is_active
    ON cat_trd_warehouse(is_active);

CREATE OR REPLACE FUNCTION trd_warehouse_refresh_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := NULLIF(
        CONCAT_WS(
            ' — ',
            NULLIF(BTRIM(NEW.name), ''),
            NULLIF(BTRIM(NEW.address), '')),
        '');
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_cat_trd_warehouse_refresh_display
    ON cat_trd_warehouse;

CREATE TRIGGER trg_cat_trd_warehouse_refresh_display
BEFORE INSERT OR UPDATE OF name, address ON cat_trd_warehouse
FOR EACH ROW
EXECUTE FUNCTION trd_warehouse_refresh_display();

CREATE TABLE IF NOT EXISTS cat_trd_payment_terms (
    catalog_id   uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display      text NOT NULL,
    code         text NOT NULL,
    name         text NOT NULL,
    due_days     integer NOT NULL,
    is_active    boolean NOT NULL,

    CONSTRAINT ck_cat_trd_payment_terms__code
        CHECK (btrim(code) <> ''),
    CONSTRAINT ck_cat_trd_payment_terms__due_days
        CHECK (due_days >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_trd_payment_terms__code
    ON cat_trd_payment_terms(code);

CREATE INDEX IF NOT EXISTS ix_cat_trd_payment_terms__display
    ON cat_trd_payment_terms(display);

CREATE INDEX IF NOT EXISTS ix_cat_trd_payment_terms__name
    ON cat_trd_payment_terms(name);

CREATE INDEX IF NOT EXISTS ix_cat_trd_payment_terms__due_days
    ON cat_trd_payment_terms(due_days);

CREATE INDEX IF NOT EXISTS ix_cat_trd_payment_terms__is_active
    ON cat_trd_payment_terms(is_active);

CREATE TABLE IF NOT EXISTS cat_trd_inventory_adjustment_reason (
    catalog_id          uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display             text NOT NULL,
    code                text NOT NULL,
    name                text NOT NULL,
    gl_behavior_hint    text NULL,
    is_active           boolean NOT NULL,

    CONSTRAINT ck_cat_trd_inventory_adjustment_reason__code
        CHECK (btrim(code) <> '')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cat_trd_inventory_adjustment_reason__code
    ON cat_trd_inventory_adjustment_reason(code);

CREATE INDEX IF NOT EXISTS ix_cat_trd_inventory_adjustment_reason__display
    ON cat_trd_inventory_adjustment_reason(display);

CREATE INDEX IF NOT EXISTS ix_cat_trd_inventory_adjustment_reason__name
    ON cat_trd_inventory_adjustment_reason(name);

CREATE INDEX IF NOT EXISTS ix_cat_trd_inventory_adjustment_reason__is_active
    ON cat_trd_inventory_adjustment_reason(is_active);

CREATE TABLE IF NOT EXISTS cat_trd_accounting_policy (
    catalog_id                        uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display                           text NOT NULL,
    cash_account_id                   uuid NULL REFERENCES accounting_accounts(account_id),
    ar_account_id                     uuid NULL REFERENCES accounting_accounts(account_id),
    inventory_account_id              uuid NULL REFERENCES accounting_accounts(account_id),
    ap_account_id                     uuid NULL REFERENCES accounting_accounts(account_id),
    sales_revenue_account_id          uuid NULL REFERENCES accounting_accounts(account_id),
    cogs_account_id                   uuid NULL REFERENCES accounting_accounts(account_id),
    inventory_adjustment_account_id   uuid NULL REFERENCES accounting_accounts(account_id),
    inventory_movements_register_id   uuid NULL REFERENCES operational_registers(register_id),
    item_prices_register_id           uuid NULL REFERENCES reference_registers(register_id)
);

CREATE INDEX IF NOT EXISTS ix_cat_trd_accounting_policy__display
    ON cat_trd_accounting_policy(display);

CREATE INDEX IF NOT EXISTS ix_cat_trd_accounting_policy__cash_account_id
    ON cat_trd_accounting_policy(cash_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_trd_accounting_policy__ar_account_id
    ON cat_trd_accounting_policy(ar_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_trd_accounting_policy__inventory_account_id
    ON cat_trd_accounting_policy(inventory_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_trd_accounting_policy__ap_account_id
    ON cat_trd_accounting_policy(ap_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_trd_accounting_policy__sales_revenue_account_id
    ON cat_trd_accounting_policy(sales_revenue_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_trd_accounting_policy__cogs_account_id
    ON cat_trd_accounting_policy(cogs_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_trd_accounting_policy__inventory_adjustment_account_id
    ON cat_trd_accounting_policy(inventory_adjustment_account_id);

-- -----------------------------------------------------------------------------
-- Documents
-- -----------------------------------------------------------------------------

CREATE OR REPLACE FUNCTION trd_build_document_display(
    fallback_label text,
    target_document_id uuid,
    business_date date)
RETURNS text
LANGUAGE plpgsql
AS $$
DECLARE
    doc_number text;
BEGIN
    SELECT NULLIF(BTRIM(number), '')
      INTO doc_number
      FROM documents
     WHERE id = target_document_id;

    RETURN CONCAT_WS(
        ' ',
        NULLIF(BTRIM(fallback_label), ''),
        doc_number,
        CASE
            WHEN business_date IS NULL THEN NULL
            ELSE TO_CHAR(business_date, 'FMMM/FMDD/YYYY')
        END);
END;
$$;

CREATE TABLE IF NOT EXISTS doc_trd_purchase_receipt (
    document_id         uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display             text NULL,
    document_date_utc   date NOT NULL,
    vendor_id           uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    warehouse_id        uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    notes               text NULL,
    amount              numeric(18, 4) NOT NULL DEFAULT 0,

    CONSTRAINT ck_doc_trd_purchase_receipt__amount
        CHECK (amount >= 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_trd_purchase_receipt__display
    ON doc_trd_purchase_receipt(display);

CREATE INDEX IF NOT EXISTS ix_doc_trd_purchase_receipt__document_date_utc
    ON doc_trd_purchase_receipt(document_date_utc);

CREATE INDEX IF NOT EXISTS ix_doc_trd_purchase_receipt__vendor_id
    ON doc_trd_purchase_receipt(vendor_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_purchase_receipt__warehouse_id
    ON doc_trd_purchase_receipt(warehouse_id);

CREATE TABLE IF NOT EXISTS doc_trd_purchase_receipt__lines (
    document_id      uuid NOT NULL,
    ordinal          integer NOT NULL,
    item_id          uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    quantity         numeric(18, 4) NOT NULL,
    unit_cost        numeric(18, 4) NOT NULL,
    line_amount      numeric(18, 4) NOT NULL,

    CONSTRAINT fk_doc_trd_purchase_receipt__lines__document
        FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
    CONSTRAINT fk_doc_trd_purchase_receipt__lines__head
        FOREIGN KEY (document_id) REFERENCES doc_trd_purchase_receipt(document_id) ON DELETE CASCADE,
    CONSTRAINT ck_doc_trd_purchase_receipt__lines__ordinal
        CHECK (ordinal > 0),
    CONSTRAINT ck_doc_trd_purchase_receipt__lines__quantity
        CHECK (quantity > 0),
    CONSTRAINT ck_doc_trd_purchase_receipt__lines__unit_cost
        CHECK (unit_cost > 0),
    CONSTRAINT ck_doc_trd_purchase_receipt__lines__line_amount
        CHECK (line_amount > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_trd_purchase_receipt__lines__document_ordinal
    ON doc_trd_purchase_receipt__lines(document_id, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_trd_purchase_receipt__lines__document_id
    ON doc_trd_purchase_receipt__lines(document_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_purchase_receipt__lines__item_id
    ON doc_trd_purchase_receipt__lines(item_id);

CREATE TABLE IF NOT EXISTS doc_trd_sales_invoice (
    document_id         uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display             text NULL,
    document_date_utc   date NOT NULL,
    customer_id         uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    warehouse_id        uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    price_type_id       uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    notes               text NULL,
    amount              numeric(18, 4) NOT NULL DEFAULT 0,

    CONSTRAINT ck_doc_trd_sales_invoice__amount
        CHECK (amount >= 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_trd_sales_invoice__display
    ON doc_trd_sales_invoice(display);

CREATE INDEX IF NOT EXISTS ix_doc_trd_sales_invoice__document_date_utc
    ON doc_trd_sales_invoice(document_date_utc);

CREATE INDEX IF NOT EXISTS ix_doc_trd_sales_invoice__customer_id
    ON doc_trd_sales_invoice(customer_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_sales_invoice__warehouse_id
    ON doc_trd_sales_invoice(warehouse_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_sales_invoice__price_type_id
    ON doc_trd_sales_invoice(price_type_id);

CREATE TABLE IF NOT EXISTS doc_trd_sales_invoice__lines (
    document_id      uuid NOT NULL,
    ordinal          integer NOT NULL,
    item_id          uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    quantity         numeric(18, 4) NOT NULL,
    unit_price       numeric(18, 4) NOT NULL,
    unit_cost        numeric(18, 4) NOT NULL,
    line_amount      numeric(18, 4) NOT NULL,

    CONSTRAINT fk_doc_trd_sales_invoice__lines__document
        FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
    CONSTRAINT fk_doc_trd_sales_invoice__lines__head
        FOREIGN KEY (document_id) REFERENCES doc_trd_sales_invoice(document_id) ON DELETE CASCADE,
    CONSTRAINT ck_doc_trd_sales_invoice__lines__ordinal
        CHECK (ordinal > 0),
    CONSTRAINT ck_doc_trd_sales_invoice__lines__quantity
        CHECK (quantity > 0),
    CONSTRAINT ck_doc_trd_sales_invoice__lines__unit_price
        CHECK (unit_price > 0),
    CONSTRAINT ck_doc_trd_sales_invoice__lines__unit_cost
        CHECK (unit_cost > 0),
    CONSTRAINT ck_doc_trd_sales_invoice__lines__line_amount
        CHECK (line_amount > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_trd_sales_invoice__lines__document_ordinal
    ON doc_trd_sales_invoice__lines(document_id, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_trd_sales_invoice__lines__document_id
    ON doc_trd_sales_invoice__lines(document_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_sales_invoice__lines__item_id
    ON doc_trd_sales_invoice__lines(item_id);

CREATE TABLE IF NOT EXISTS doc_trd_customer_payment (
    document_id         uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display             text NULL,
    document_date_utc   date NOT NULL,
    customer_id         uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    cash_account_id     uuid NULL REFERENCES accounting_accounts(account_id),
    sales_invoice_id    uuid NULL REFERENCES doc_trd_sales_invoice(document_id) ON DELETE RESTRICT,
    amount              numeric(18, 4) NOT NULL,
    notes               text NULL,

    CONSTRAINT ck_doc_trd_customer_payment__amount
        CHECK (amount > 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_trd_customer_payment__display
    ON doc_trd_customer_payment(display);

CREATE INDEX IF NOT EXISTS ix_doc_trd_customer_payment__document_date_utc
    ON doc_trd_customer_payment(document_date_utc);

CREATE INDEX IF NOT EXISTS ix_doc_trd_customer_payment__customer_id
    ON doc_trd_customer_payment(customer_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_customer_payment__cash_account_id
    ON doc_trd_customer_payment(cash_account_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_customer_payment__sales_invoice_id
    ON doc_trd_customer_payment(sales_invoice_id);

CREATE TABLE IF NOT EXISTS doc_trd_vendor_payment (
    document_id           uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display               text NULL,
    document_date_utc     date NOT NULL,
    vendor_id             uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    cash_account_id       uuid NULL REFERENCES accounting_accounts(account_id),
    purchase_receipt_id   uuid NULL REFERENCES doc_trd_purchase_receipt(document_id) ON DELETE RESTRICT,
    amount                numeric(18, 4) NOT NULL,
    notes                 text NULL,

    CONSTRAINT ck_doc_trd_vendor_payment__amount
        CHECK (amount > 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_trd_vendor_payment__display
    ON doc_trd_vendor_payment(display);

CREATE INDEX IF NOT EXISTS ix_doc_trd_vendor_payment__document_date_utc
    ON doc_trd_vendor_payment(document_date_utc);

CREATE INDEX IF NOT EXISTS ix_doc_trd_vendor_payment__vendor_id
    ON doc_trd_vendor_payment(vendor_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_vendor_payment__cash_account_id
    ON doc_trd_vendor_payment(cash_account_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_vendor_payment__purchase_receipt_id
    ON doc_trd_vendor_payment(purchase_receipt_id);

CREATE TABLE IF NOT EXISTS doc_trd_inventory_transfer (
    document_id           uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display               text NULL,
    document_date_utc     date NOT NULL,
    from_warehouse_id     uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    to_warehouse_id       uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    notes                 text NULL
);

CREATE INDEX IF NOT EXISTS ix_doc_trd_inventory_transfer__display
    ON doc_trd_inventory_transfer(display);

CREATE INDEX IF NOT EXISTS ix_doc_trd_inventory_transfer__document_date_utc
    ON doc_trd_inventory_transfer(document_date_utc);

CREATE INDEX IF NOT EXISTS ix_doc_trd_inventory_transfer__from_warehouse_id
    ON doc_trd_inventory_transfer(from_warehouse_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_inventory_transfer__to_warehouse_id
    ON doc_trd_inventory_transfer(to_warehouse_id);

CREATE TABLE IF NOT EXISTS doc_trd_inventory_transfer__lines (
    document_id      uuid NOT NULL,
    ordinal          integer NOT NULL,
    item_id          uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    quantity         numeric(18, 4) NOT NULL,

    CONSTRAINT fk_doc_trd_inventory_transfer__lines__document
        FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
    CONSTRAINT fk_doc_trd_inventory_transfer__lines__head
        FOREIGN KEY (document_id) REFERENCES doc_trd_inventory_transfer(document_id) ON DELETE CASCADE,
    CONSTRAINT ck_doc_trd_inventory_transfer__lines__ordinal
        CHECK (ordinal > 0),
    CONSTRAINT ck_doc_trd_inventory_transfer__lines__quantity
        CHECK (quantity > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_trd_inventory_transfer__lines__document_ordinal
    ON doc_trd_inventory_transfer__lines(document_id, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_trd_inventory_transfer__lines__document_id
    ON doc_trd_inventory_transfer__lines(document_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_inventory_transfer__lines__item_id
    ON doc_trd_inventory_transfer__lines(item_id);

CREATE TABLE IF NOT EXISTS doc_trd_inventory_adjustment (
    document_id         uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display             text NULL,
    document_date_utc   date NOT NULL,
    warehouse_id        uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    reason_id           uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    notes               text NULL,
    amount              numeric(18, 4) NOT NULL DEFAULT 0,

    CONSTRAINT ck_doc_trd_inventory_adjustment__amount
        CHECK (amount >= 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_trd_inventory_adjustment__display
    ON doc_trd_inventory_adjustment(display);

CREATE INDEX IF NOT EXISTS ix_doc_trd_inventory_adjustment__document_date_utc
    ON doc_trd_inventory_adjustment(document_date_utc);

CREATE INDEX IF NOT EXISTS ix_doc_trd_inventory_adjustment__warehouse_id
    ON doc_trd_inventory_adjustment(warehouse_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_inventory_adjustment__reason_id
    ON doc_trd_inventory_adjustment(reason_id);

CREATE TABLE IF NOT EXISTS doc_trd_inventory_adjustment__lines (
    document_id      uuid NOT NULL,
    ordinal          integer NOT NULL,
    item_id          uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    quantity_delta   numeric(18, 4) NOT NULL,
    unit_cost        numeric(18, 4) NOT NULL,
    line_amount      numeric(18, 4) NOT NULL,

    CONSTRAINT fk_doc_trd_inventory_adjustment__lines__document
        FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
    CONSTRAINT fk_doc_trd_inventory_adjustment__lines__head
        FOREIGN KEY (document_id) REFERENCES doc_trd_inventory_adjustment(document_id) ON DELETE CASCADE,
    CONSTRAINT ck_doc_trd_inventory_adjustment__lines__ordinal
        CHECK (ordinal > 0),
    CONSTRAINT ck_doc_trd_inventory_adjustment__lines__quantity_delta
        CHECK (quantity_delta <> 0),
    CONSTRAINT ck_doc_trd_inventory_adjustment__lines__unit_cost
        CHECK (unit_cost > 0),
    CONSTRAINT ck_doc_trd_inventory_adjustment__lines__line_amount
        CHECK (line_amount > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_trd_inventory_adjustment__lines__document_ordinal
    ON doc_trd_inventory_adjustment__lines(document_id, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_trd_inventory_adjustment__lines__document_id
    ON doc_trd_inventory_adjustment__lines(document_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_inventory_adjustment__lines__item_id
    ON doc_trd_inventory_adjustment__lines(item_id);

CREATE TABLE IF NOT EXISTS doc_trd_customer_return (
    document_id         uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display             text NULL,
    document_date_utc   date NOT NULL,
    customer_id         uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    warehouse_id        uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    sales_invoice_id    uuid NULL REFERENCES doc_trd_sales_invoice(document_id) ON DELETE RESTRICT,
    notes               text NULL,
    amount              numeric(18, 4) NOT NULL DEFAULT 0,

    CONSTRAINT ck_doc_trd_customer_return__amount
        CHECK (amount >= 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_trd_customer_return__display
    ON doc_trd_customer_return(display);

CREATE INDEX IF NOT EXISTS ix_doc_trd_customer_return__document_date_utc
    ON doc_trd_customer_return(document_date_utc);

CREATE INDEX IF NOT EXISTS ix_doc_trd_customer_return__customer_id
    ON doc_trd_customer_return(customer_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_customer_return__warehouse_id
    ON doc_trd_customer_return(warehouse_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_customer_return__sales_invoice_id
    ON doc_trd_customer_return(sales_invoice_id);

CREATE TABLE IF NOT EXISTS doc_trd_customer_return__lines (
    document_id      uuid NOT NULL,
    ordinal          integer NOT NULL,
    item_id          uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    quantity         numeric(18, 4) NOT NULL,
    unit_price       numeric(18, 4) NOT NULL,
    unit_cost        numeric(18, 4) NOT NULL,
    line_amount      numeric(18, 4) NOT NULL,

    CONSTRAINT fk_doc_trd_customer_return__lines__document
        FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
    CONSTRAINT fk_doc_trd_customer_return__lines__head
        FOREIGN KEY (document_id) REFERENCES doc_trd_customer_return(document_id) ON DELETE CASCADE,
    CONSTRAINT ck_doc_trd_customer_return__lines__ordinal
        CHECK (ordinal > 0),
    CONSTRAINT ck_doc_trd_customer_return__lines__quantity
        CHECK (quantity > 0),
    CONSTRAINT ck_doc_trd_customer_return__lines__unit_price
        CHECK (unit_price > 0),
    CONSTRAINT ck_doc_trd_customer_return__lines__unit_cost
        CHECK (unit_cost > 0),
    CONSTRAINT ck_doc_trd_customer_return__lines__line_amount
        CHECK (line_amount > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_trd_customer_return__lines__document_ordinal
    ON doc_trd_customer_return__lines(document_id, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_trd_customer_return__lines__document_id
    ON doc_trd_customer_return__lines(document_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_customer_return__lines__item_id
    ON doc_trd_customer_return__lines(item_id);

CREATE TABLE IF NOT EXISTS doc_trd_vendor_return (
    document_id           uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display               text NULL,
    document_date_utc     date NOT NULL,
    vendor_id             uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    warehouse_id          uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    purchase_receipt_id   uuid NULL REFERENCES doc_trd_purchase_receipt(document_id) ON DELETE RESTRICT,
    notes                 text NULL,
    amount                numeric(18, 4) NOT NULL DEFAULT 0,

    CONSTRAINT ck_doc_trd_vendor_return__amount
        CHECK (amount >= 0)
);

CREATE INDEX IF NOT EXISTS ix_doc_trd_vendor_return__display
    ON doc_trd_vendor_return(display);

CREATE INDEX IF NOT EXISTS ix_doc_trd_vendor_return__document_date_utc
    ON doc_trd_vendor_return(document_date_utc);

CREATE INDEX IF NOT EXISTS ix_doc_trd_vendor_return__vendor_id
    ON doc_trd_vendor_return(vendor_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_vendor_return__warehouse_id
    ON doc_trd_vendor_return(warehouse_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_vendor_return__purchase_receipt_id
    ON doc_trd_vendor_return(purchase_receipt_id);

CREATE TABLE IF NOT EXISTS doc_trd_vendor_return__lines (
    document_id      uuid NOT NULL,
    ordinal          integer NOT NULL,
    item_id          uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    quantity         numeric(18, 4) NOT NULL,
    unit_cost        numeric(18, 4) NOT NULL,
    line_amount      numeric(18, 4) NOT NULL,

    CONSTRAINT fk_doc_trd_vendor_return__lines__document
        FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
    CONSTRAINT fk_doc_trd_vendor_return__lines__head
        FOREIGN KEY (document_id) REFERENCES doc_trd_vendor_return(document_id) ON DELETE CASCADE,
    CONSTRAINT ck_doc_trd_vendor_return__lines__ordinal
        CHECK (ordinal > 0),
    CONSTRAINT ck_doc_trd_vendor_return__lines__quantity
        CHECK (quantity > 0),
    CONSTRAINT ck_doc_trd_vendor_return__lines__unit_cost
        CHECK (unit_cost > 0),
    CONSTRAINT ck_doc_trd_vendor_return__lines__line_amount
        CHECK (line_amount > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_trd_vendor_return__lines__document_ordinal
    ON doc_trd_vendor_return__lines(document_id, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_trd_vendor_return__lines__document_id
    ON doc_trd_vendor_return__lines(document_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_vendor_return__lines__item_id
    ON doc_trd_vendor_return__lines(item_id);

CREATE TABLE IF NOT EXISTS doc_trd_item_price_update (
    document_id      uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display          text NULL,
    effective_date   date NOT NULL,
    notes            text NULL
);

CREATE INDEX IF NOT EXISTS ix_doc_trd_item_price_update__display
    ON doc_trd_item_price_update(display);

CREATE INDEX IF NOT EXISTS ix_doc_trd_item_price_update__effective_date
    ON doc_trd_item_price_update(effective_date);

CREATE TABLE IF NOT EXISTS doc_trd_item_price_update__lines (
    document_id      uuid NOT NULL,
    ordinal          integer NOT NULL,
    item_id          uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    price_type_id    uuid NOT NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    currency         text NOT NULL,
    unit_price       numeric(18, 4) NOT NULL,

    CONSTRAINT fk_doc_trd_item_price_update__lines__document
        FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
    CONSTRAINT fk_doc_trd_item_price_update__lines__head
        FOREIGN KEY (document_id) REFERENCES doc_trd_item_price_update(document_id) ON DELETE CASCADE,
    CONSTRAINT ck_doc_trd_item_price_update__lines__ordinal
        CHECK (ordinal > 0),
    CONSTRAINT ck_doc_trd_item_price_update__lines__currency
        CHECK (btrim(currency) <> ''),
    CONSTRAINT ck_doc_trd_item_price_update__lines__unit_price
        CHECK (unit_price >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_trd_item_price_update__lines__document_ordinal
    ON doc_trd_item_price_update__lines(document_id, ordinal);

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_trd_item_price_update__lines__document_item_price_type_currency
    ON doc_trd_item_price_update__lines(document_id, item_id, price_type_id, currency);

CREATE INDEX IF NOT EXISTS ix_doc_trd_item_price_update__lines__document_id
    ON doc_trd_item_price_update__lines(document_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_item_price_update__lines__item_id
    ON doc_trd_item_price_update__lines(item_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_item_price_update__lines__price_type_id
    ON doc_trd_item_price_update__lines(price_type_id);

CREATE INDEX IF NOT EXISTS ix_doc_trd_item_price_update__lines__currency
    ON doc_trd_item_price_update__lines(currency);

CREATE OR REPLACE FUNCTION trd_compute_document_amount(
    line_table_name text,
    target_document_id uuid)
RETURNS numeric(18, 4)
LANGUAGE plpgsql
AS $$
DECLARE
    total_amount numeric(18, 4);
BEGIN
    EXECUTE format(
        'SELECT COALESCE(SUM(line_amount), 0)::numeric(18,4) FROM %I WHERE document_id = $1',
        line_table_name)
        INTO total_amount
        USING target_document_id;

    RETURN COALESCE(total_amount, 0)::numeric(18, 4);
END;
$$;

CREATE OR REPLACE FUNCTION trd_refresh_document_amount()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.amount := trd_compute_document_amount(TG_ARGV[0], NEW.document_id);
    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION trd_touch_document_amount_from_lines()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP IN ('UPDATE', 'DELETE') AND OLD.document_id IS NOT NULL THEN
        EXECUTE format(
            'UPDATE %I SET amount = amount WHERE document_id = $1',
            TG_ARGV[0])
            USING OLD.document_id;
    END IF;

    IF TG_OP IN ('INSERT', 'UPDATE')
       AND NEW.document_id IS NOT NULL
       AND (TG_OP = 'INSERT' OR NEW.document_id IS DISTINCT FROM OLD.document_id) THEN
        EXECUTE format(
            'UPDATE %I SET amount = amount WHERE document_id = $1',
            TG_ARGV[0])
            USING NEW.document_id;
    END IF;

    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_trd_purchase_receipt_refresh_amount
    ON doc_trd_purchase_receipt;

CREATE TRIGGER trg_doc_trd_purchase_receipt_refresh_amount
BEFORE INSERT OR UPDATE OF amount ON doc_trd_purchase_receipt
FOR EACH ROW
EXECUTE FUNCTION trd_refresh_document_amount('doc_trd_purchase_receipt__lines');

DROP TRIGGER IF EXISTS trg_doc_trd_sales_invoice_refresh_amount
    ON doc_trd_sales_invoice;

CREATE TRIGGER trg_doc_trd_sales_invoice_refresh_amount
BEFORE INSERT OR UPDATE OF amount ON doc_trd_sales_invoice
FOR EACH ROW
EXECUTE FUNCTION trd_refresh_document_amount('doc_trd_sales_invoice__lines');

DROP TRIGGER IF EXISTS trg_doc_trd_inventory_adjustment_refresh_amount
    ON doc_trd_inventory_adjustment;

CREATE TRIGGER trg_doc_trd_inventory_adjustment_refresh_amount
BEFORE INSERT OR UPDATE OF amount ON doc_trd_inventory_adjustment
FOR EACH ROW
EXECUTE FUNCTION trd_refresh_document_amount('doc_trd_inventory_adjustment__lines');

DROP TRIGGER IF EXISTS trg_doc_trd_customer_return_refresh_amount
    ON doc_trd_customer_return;

CREATE TRIGGER trg_doc_trd_customer_return_refresh_amount
BEFORE INSERT OR UPDATE OF amount ON doc_trd_customer_return
FOR EACH ROW
EXECUTE FUNCTION trd_refresh_document_amount('doc_trd_customer_return__lines');

DROP TRIGGER IF EXISTS trg_doc_trd_vendor_return_refresh_amount
    ON doc_trd_vendor_return;

CREATE TRIGGER trg_doc_trd_vendor_return_refresh_amount
BEFORE INSERT OR UPDATE OF amount ON doc_trd_vendor_return
FOR EACH ROW
EXECUTE FUNCTION trd_refresh_document_amount('doc_trd_vendor_return__lines');

DROP TRIGGER IF EXISTS trg_doc_trd_purchase_receipt__lines_touch_amount
    ON doc_trd_purchase_receipt__lines;

CREATE TRIGGER trg_doc_trd_purchase_receipt__lines_touch_amount
AFTER INSERT OR UPDATE OR DELETE ON doc_trd_purchase_receipt__lines
FOR EACH ROW
EXECUTE FUNCTION trd_touch_document_amount_from_lines('doc_trd_purchase_receipt');

DROP TRIGGER IF EXISTS trg_doc_trd_sales_invoice__lines_touch_amount
    ON doc_trd_sales_invoice__lines;

CREATE TRIGGER trg_doc_trd_sales_invoice__lines_touch_amount
AFTER INSERT OR UPDATE OR DELETE ON doc_trd_sales_invoice__lines
FOR EACH ROW
EXECUTE FUNCTION trd_touch_document_amount_from_lines('doc_trd_sales_invoice');

DROP TRIGGER IF EXISTS trg_doc_trd_inventory_adjustment__lines_touch_amount
    ON doc_trd_inventory_adjustment__lines;

CREATE TRIGGER trg_doc_trd_inventory_adjustment__lines_touch_amount
AFTER INSERT OR UPDATE OR DELETE ON doc_trd_inventory_adjustment__lines
FOR EACH ROW
EXECUTE FUNCTION trd_touch_document_amount_from_lines('doc_trd_inventory_adjustment');

DROP TRIGGER IF EXISTS trg_doc_trd_customer_return__lines_touch_amount
    ON doc_trd_customer_return__lines;

CREATE TRIGGER trg_doc_trd_customer_return__lines_touch_amount
AFTER INSERT OR UPDATE OR DELETE ON doc_trd_customer_return__lines
FOR EACH ROW
EXECUTE FUNCTION trd_touch_document_amount_from_lines('doc_trd_customer_return');

DROP TRIGGER IF EXISTS trg_doc_trd_vendor_return__lines_touch_amount
    ON doc_trd_vendor_return__lines;

CREATE TRIGGER trg_doc_trd_vendor_return__lines_touch_amount
AFTER INSERT OR UPDATE OR DELETE ON doc_trd_vendor_return__lines
FOR EACH ROW
EXECUTE FUNCTION trd_touch_document_amount_from_lines('doc_trd_vendor_return');

DROP TRIGGER IF EXISTS trg_docrel_mirror__sales_invoice_id__4877a2c8
    ON doc_trd_customer_payment;

CREATE TRIGGER trg_docrel_mirror__sales_invoice_id__4877a2c8
AFTER INSERT OR UPDATE OR DELETE ON doc_trd_customer_payment
FOR EACH ROW
EXECUTE FUNCTION ngb_sync_mirrored_document_relationship('sales_invoice_id', 'based_on');

DROP TRIGGER IF EXISTS trg_docrel_mirror__purchase_receipt_id__9efbafb3
    ON doc_trd_vendor_payment;

CREATE TRIGGER trg_docrel_mirror__purchase_receipt_id__9efbafb3
AFTER INSERT OR UPDATE OR DELETE ON doc_trd_vendor_payment
FOR EACH ROW
EXECUTE FUNCTION ngb_sync_mirrored_document_relationship('purchase_receipt_id', 'based_on');

DROP TRIGGER IF EXISTS trg_docrel_mirror__sales_invoice_id__4877a2c8
    ON doc_trd_customer_return;

CREATE TRIGGER trg_docrel_mirror__sales_invoice_id__4877a2c8
AFTER INSERT OR UPDATE OR DELETE ON doc_trd_customer_return
FOR EACH ROW
EXECUTE FUNCTION ngb_sync_mirrored_document_relationship('sales_invoice_id', 'based_on');

DROP TRIGGER IF EXISTS trg_docrel_mirror__purchase_receipt_id__9efbafb3
    ON doc_trd_vendor_return;

CREATE TRIGGER trg_docrel_mirror__purchase_receipt_id__9efbafb3
AFTER INSERT OR UPDATE OR DELETE ON doc_trd_vendor_return
FOR EACH ROW
EXECUTE FUNCTION ngb_sync_mirrored_document_relationship('purchase_receipt_id', 'based_on');

CREATE OR REPLACE FUNCTION trd_purchase_receipt_refresh_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := trd_build_document_display('Purchase Receipt', NEW.document_id, NEW.document_date_utc);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_trd_purchase_receipt_refresh_display
    ON doc_trd_purchase_receipt;

CREATE TRIGGER trg_doc_trd_purchase_receipt_refresh_display
BEFORE INSERT OR UPDATE OF document_date_utc, display ON doc_trd_purchase_receipt
FOR EACH ROW
EXECUTE FUNCTION trd_purchase_receipt_refresh_display();

CREATE OR REPLACE FUNCTION trd_sales_invoice_refresh_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := trd_build_document_display('Sales Invoice', NEW.document_id, NEW.document_date_utc);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_trd_sales_invoice_refresh_display
    ON doc_trd_sales_invoice;

CREATE TRIGGER trg_doc_trd_sales_invoice_refresh_display
BEFORE INSERT OR UPDATE OF document_date_utc, display ON doc_trd_sales_invoice
FOR EACH ROW
EXECUTE FUNCTION trd_sales_invoice_refresh_display();

CREATE OR REPLACE FUNCTION trd_customer_payment_refresh_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := trd_build_document_display('Customer Payment', NEW.document_id, NEW.document_date_utc);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_trd_customer_payment_refresh_display
    ON doc_trd_customer_payment;

CREATE TRIGGER trg_doc_trd_customer_payment_refresh_display
BEFORE INSERT OR UPDATE OF document_date_utc, display ON doc_trd_customer_payment
FOR EACH ROW
EXECUTE FUNCTION trd_customer_payment_refresh_display();

CREATE OR REPLACE FUNCTION trd_vendor_payment_refresh_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := trd_build_document_display('Vendor Payment', NEW.document_id, NEW.document_date_utc);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_trd_vendor_payment_refresh_display
    ON doc_trd_vendor_payment;

CREATE TRIGGER trg_doc_trd_vendor_payment_refresh_display
BEFORE INSERT OR UPDATE OF document_date_utc, display ON doc_trd_vendor_payment
FOR EACH ROW
EXECUTE FUNCTION trd_vendor_payment_refresh_display();

CREATE OR REPLACE FUNCTION trd_inventory_transfer_refresh_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := trd_build_document_display('Inventory Transfer', NEW.document_id, NEW.document_date_utc);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_trd_inventory_transfer_refresh_display
    ON doc_trd_inventory_transfer;

CREATE TRIGGER trg_doc_trd_inventory_transfer_refresh_display
BEFORE INSERT OR UPDATE OF document_date_utc, display ON doc_trd_inventory_transfer
FOR EACH ROW
EXECUTE FUNCTION trd_inventory_transfer_refresh_display();

CREATE OR REPLACE FUNCTION trd_inventory_adjustment_refresh_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := trd_build_document_display('Inventory Adjustment', NEW.document_id, NEW.document_date_utc);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_trd_inventory_adjustment_refresh_display
    ON doc_trd_inventory_adjustment;

CREATE TRIGGER trg_doc_trd_inventory_adjustment_refresh_display
BEFORE INSERT OR UPDATE OF document_date_utc, display ON doc_trd_inventory_adjustment
FOR EACH ROW
EXECUTE FUNCTION trd_inventory_adjustment_refresh_display();

CREATE OR REPLACE FUNCTION trd_customer_return_refresh_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := trd_build_document_display('Customer Return', NEW.document_id, NEW.document_date_utc);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_trd_customer_return_refresh_display
    ON doc_trd_customer_return;

CREATE TRIGGER trg_doc_trd_customer_return_refresh_display
BEFORE INSERT OR UPDATE OF document_date_utc, display ON doc_trd_customer_return
FOR EACH ROW
EXECUTE FUNCTION trd_customer_return_refresh_display();

CREATE OR REPLACE FUNCTION trd_vendor_return_refresh_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := trd_build_document_display('Vendor Return', NEW.document_id, NEW.document_date_utc);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_trd_vendor_return_refresh_display
    ON doc_trd_vendor_return;

CREATE TRIGGER trg_doc_trd_vendor_return_refresh_display
BEFORE INSERT OR UPDATE OF document_date_utc, display ON doc_trd_vendor_return
FOR EACH ROW
EXECUTE FUNCTION trd_vendor_return_refresh_display();

CREATE OR REPLACE FUNCTION trd_item_price_update_refresh_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := trd_build_document_display('Item Price Update', NEW.document_id, NEW.effective_date);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_trd_item_price_update_refresh_display
    ON doc_trd_item_price_update;

CREATE TRIGGER trg_doc_trd_item_price_update_refresh_display
BEFORE INSERT OR UPDATE OF effective_date, display ON doc_trd_item_price_update
FOR EACH ROW
EXECUTE FUNCTION trd_item_price_update_refresh_display();

CREATE OR REPLACE FUNCTION trd_refresh_document_display_from_header()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    CASE NEW.type_code
        WHEN 'trd.purchase_receipt' THEN
            UPDATE doc_trd_purchase_receipt
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'trd.sales_invoice' THEN
            UPDATE doc_trd_sales_invoice
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'trd.customer_payment' THEN
            UPDATE doc_trd_customer_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'trd.vendor_payment' THEN
            UPDATE doc_trd_vendor_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'trd.inventory_transfer' THEN
            UPDATE doc_trd_inventory_transfer
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'trd.inventory_adjustment' THEN
            UPDATE doc_trd_inventory_adjustment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'trd.customer_return' THEN
            UPDATE doc_trd_customer_return
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'trd.vendor_return' THEN
            UPDATE doc_trd_vendor_return
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'trd.item_price_update' THEN
            UPDATE doc_trd_item_price_update
               SET display = display
             WHERE document_id = NEW.id;
    END CASE;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_documents__trd_refresh_typed_display ON documents;

CREATE TRIGGER trg_documents__trd_refresh_typed_display
AFTER UPDATE OF number ON documents
FOR EACH ROW
WHEN (NEW.type_code IN ('trd.purchase_receipt', 'trd.sales_invoice', 'trd.customer_payment', 'trd.vendor_payment', 'trd.inventory_transfer', 'trd.inventory_adjustment', 'trd.customer_return', 'trd.vendor_return', 'trd.item_price_update'))
EXECUTE FUNCTION trd_refresh_document_display_from_header();

-- Every Trade typed document table must be guarded by the shared posted-document immutability trigger.
-- The platform pack provides the installer function; Trade pack depends on platform.
SELECT ngb_install_typed_document_immutability_guards();
