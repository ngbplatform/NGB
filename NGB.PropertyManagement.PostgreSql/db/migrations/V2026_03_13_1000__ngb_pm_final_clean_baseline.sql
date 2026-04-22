-- NGB.PropertyManagement final clean baseline for recreated databases.
--
-- Scope:
-- - final PM typed schema as of 2026-03-13 snapshot
-- - no business seed data (use migrator command: seed-defaults)
-- - intended for clean-slate / recreated DB workflow only
--
-- Notes:
-- - PM pack depends on the platform pack.
-- - Business configuration stays in the idempotent setup flow, not in schema migrations.
-- - This baseline intentionally folds the historical PM evolution into one file so
--   old PM versioned migrations can be removed for clean-slate environments.

SET TIME ZONE 'UTC';
SET search_path = public;

-- -----------------------------------------------------------------------------
-- Catalogs
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS cat_pm_party (
    catalog_id uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display    text NULL,
    email      text NULL,
    phone      text NULL
);

CREATE INDEX IF NOT EXISTS ix_cat_pm_party__display
    ON cat_pm_party(display);

CREATE TABLE IF NOT EXISTS cat_pm_property (
    catalog_id          uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    kind                text NOT NULL,
    parent_property_id  uuid NULL REFERENCES catalogs(id) ON DELETE RESTRICT,
    unit_no             text NULL,
    display             text NOT NULL,
    address_line1       text NULL,
    address_line2       text NULL,
    city                text NULL,
    state               text NULL,
    zip                 text NULL,

    CONSTRAINT ck_cat_pm_property__kind
        CHECK (kind IN ('Building', 'Unit')),
    CONSTRAINT ck_cat_pm_property__parent_by_kind
        CHECK (
            (kind = 'Building' AND parent_property_id IS NULL)
            OR
            (kind = 'Unit' AND parent_property_id IS NOT NULL)
        ),
    CONSTRAINT ck_cat_pm_property__unit_no_required_for_unit
        CHECK (kind <> 'Unit' OR unit_no IS NOT NULL),
    CONSTRAINT ck_cat_pm_property__building_address_required
        CHECK (
            kind <> 'Building'
            OR (
                address_line1 IS NOT NULL
                AND city IS NOT NULL
                AND state IS NOT NULL
                AND zip IS NOT NULL
            )
        ),
    CONSTRAINT ck_cat_pm_property__parent_not_self
        CHECK (parent_property_id IS NULL OR parent_property_id <> catalog_id)
);

CREATE INDEX IF NOT EXISTS ix_cat_pm_property__display
    ON cat_pm_property(display);

CREATE INDEX IF NOT EXISTS ix_cat_pm_property__kind
    ON cat_pm_property(kind);

CREATE INDEX IF NOT EXISTS ix_cat_pm_property__parent_property_id
    ON cat_pm_property(parent_property_id);

CREATE INDEX IF NOT EXISTS ix_cat_pm_property__parent_unit_no
    ON cat_pm_property(parent_property_id, unit_no)
    WHERE kind = 'Unit';

CREATE TABLE IF NOT EXISTS cat_pm_accounting_policy (
    catalog_id                           uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display                              text NOT NULL,
    cash_account_id                      uuid NULL REFERENCES accounting_accounts(account_id),
    ar_tenants_account_id                uuid NULL REFERENCES accounting_accounts(account_id),
    rent_income_account_id               uuid NULL REFERENCES accounting_accounts(account_id),
    tenant_balances_register_id          uuid NULL REFERENCES operational_registers(register_id),
    receivables_open_items_register_id   uuid NULL REFERENCES operational_registers(register_id)
);

CREATE INDEX IF NOT EXISTS ix_cat_pm_accounting_policy__display
    ON cat_pm_accounting_policy(display);

CREATE INDEX IF NOT EXISTS ix_cat_pm_accounting_policy__cash_account_id
    ON cat_pm_accounting_policy(cash_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_pm_accounting_policy__ar_tenants_account_id
    ON cat_pm_accounting_policy(ar_tenants_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_pm_accounting_policy__rent_income_account_id
    ON cat_pm_accounting_policy(rent_income_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_pm_accounting_policy__tenant_balances_register_id
    ON cat_pm_accounting_policy(tenant_balances_register_id);

CREATE INDEX IF NOT EXISTS ix_cat_pm_accounting_policy__receivables_open_items_register_id
    ON cat_pm_accounting_policy(receivables_open_items_register_id);

CREATE TABLE IF NOT EXISTS cat_pm_receivable_charge_type (
    catalog_id         uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display            text NOT NULL,
    credit_account_id  uuid NULL REFERENCES accounting_accounts(account_id)
);

CREATE INDEX IF NOT EXISTS ix_cat_pm_receivable_charge_type__display
    ON cat_pm_receivable_charge_type(display);

CREATE INDEX IF NOT EXISTS ix_cat_pm_receivable_charge_type__credit_account_id
    ON cat_pm_receivable_charge_type(credit_account_id);

-- -----------------------------------------------------------------------------
-- Documents
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS doc_pm_lease (
    document_id   uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display       text NOT NULL,
    property_id   uuid NOT NULL REFERENCES catalogs(id),
    start_on_utc  date NOT NULL,
    end_on_utc    date NULL,
    rent_amount   numeric(18, 2) NOT NULL,
    due_day       integer NULL,
    memo          text NULL
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_lease__display
    ON doc_pm_lease(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_lease__property_id
    ON doc_pm_lease(property_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_lease__property_active_window
    ON doc_pm_lease(property_id, start_on_utc, end_on_utc, document_id);

CREATE TABLE IF NOT EXISTS doc_pm_lease__parties (
    document_id uuid    NOT NULL,
    party_id    uuid    NOT NULL REFERENCES catalogs(id),
    role        text    NOT NULL,
    is_primary  boolean NOT NULL,
    ordinal     integer NOT NULL,

    CONSTRAINT fk_doc_pm_lease__parties__document
        FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
    CONSTRAINT fk_doc_pm_lease__parties__lease
        FOREIGN KEY (document_id) REFERENCES doc_pm_lease(document_id) ON DELETE CASCADE,

    CONSTRAINT chk_doc_pm_lease__parties__role
        CHECK (role IN ('PrimaryTenant', 'CoTenant', 'Occupant', 'Guarantor')),
    CONSTRAINT chk_doc_pm_lease__parties__ordinal
        CHECK (ordinal > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_pm_lease__parties__document_party
    ON doc_pm_lease__parties(document_id, party_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_pm_lease__parties__single_primary
    ON doc_pm_lease__parties(document_id)
    WHERE is_primary = TRUE;

CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_pm_lease__parties__document_ordinal
    ON doc_pm_lease__parties(document_id, ordinal);

CREATE INDEX IF NOT EXISTS ix_doc_pm_lease__parties__party_id
    ON doc_pm_lease__parties(party_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_lease__parties__document_id
    ON doc_pm_lease__parties(document_id);

CREATE TABLE IF NOT EXISTS doc_pm_rent_charge (
    document_id      uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display          text NOT NULL,
    lease_id         uuid NOT NULL REFERENCES doc_pm_lease(document_id),
    period_from_utc  date NOT NULL,
    period_to_utc    date NOT NULL,
    due_on_utc       date NOT NULL,
    amount           numeric(18, 4) NOT NULL,
    memo             text NULL
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_rent_charge__display
    ON doc_pm_rent_charge(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_rent_charge__lease_id
    ON doc_pm_rent_charge(lease_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_rent_charge__due_on_utc
    ON doc_pm_rent_charge(due_on_utc);

CREATE INDEX IF NOT EXISTS ix_doc_pm_rent_charge__lease_due_document
    ON doc_pm_rent_charge(lease_id, due_on_utc, document_id);

CREATE TABLE IF NOT EXISTS doc_pm_receivable_charge (
    document_id    uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display        text NOT NULL,
    lease_id       uuid NOT NULL REFERENCES doc_pm_lease(document_id),
    charge_type_id uuid NOT NULL REFERENCES catalogs(id),
    due_on_utc     date NOT NULL,
    amount         numeric(18, 4) NOT NULL,
    memo           text NULL
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_charge__display
    ON doc_pm_receivable_charge(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_charge__lease_id
    ON doc_pm_receivable_charge(lease_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_charge__charge_type_id
    ON doc_pm_receivable_charge(charge_type_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_charge__due_on_utc
    ON doc_pm_receivable_charge(due_on_utc);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_charge__lease_due_document
    ON doc_pm_receivable_charge(lease_id, due_on_utc, document_id);

CREATE TABLE IF NOT EXISTS doc_pm_receivable_payment (
    document_id      uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display          text NOT NULL,
    lease_id         uuid NOT NULL REFERENCES doc_pm_lease(document_id),
    received_on_utc  date NOT NULL,
    amount           numeric(18, 4) NOT NULL,
    memo             text NULL
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_payment__display
    ON doc_pm_receivable_payment(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_payment__lease_id
    ON doc_pm_receivable_payment(lease_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_payment__received_on_utc
    ON doc_pm_receivable_payment(received_on_utc);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_payment__lease_received_document
    ON doc_pm_receivable_payment(lease_id, received_on_utc, document_id);

CREATE TABLE IF NOT EXISTS doc_pm_receivable_apply (
    document_id        uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display            text NOT NULL,
    credit_document_id uuid NOT NULL REFERENCES documents(id),
    charge_document_id uuid NOT NULL REFERENCES documents(id),
    applied_on_utc     date NOT NULL,
    amount             numeric(18, 4) NOT NULL,
    memo               text NULL
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_apply__display
    ON doc_pm_receivable_apply(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_apply__credit_document_id
    ON doc_pm_receivable_apply(credit_document_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_apply__charge_document_id
    ON doc_pm_receivable_apply(charge_document_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_apply__applied_on_utc
    ON doc_pm_receivable_apply(applied_on_utc);

-- -----------------------------------------------------------------------------
-- Property invariants and display
-- -----------------------------------------------------------------------------

CREATE OR REPLACE FUNCTION ngb_pm_property_check_unit_no_unique(p_catalog_id uuid)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_kind text;
    v_parent uuid;
    v_unit_no text;
    v_is_deleted boolean;
BEGIN
    SELECT p.kind, p.parent_property_id, p.unit_no, c.is_deleted
      INTO v_kind, v_parent, v_unit_no, v_is_deleted
      FROM cat_pm_property p
      JOIN catalogs c ON c.id = p.catalog_id
     WHERE p.catalog_id = p_catalog_id;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    IF v_kind <> 'Unit' OR v_is_deleted = TRUE THEN
        RETURN;
    END IF;

    IF EXISTS (
        SELECT 1
          FROM cat_pm_property p2
          JOIN catalogs c2 ON c2.id = p2.catalog_id
         WHERE p2.kind = 'Unit'
           AND p2.parent_property_id = v_parent
           AND p2.unit_no = v_unit_no
           AND c2.is_deleted = FALSE
           AND p2.catalog_id <> p_catalog_id
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23505',
            MESSAGE = format('Duplicate pm.property unit_no within building (building_id=%s, unit_no=%s).', v_parent, v_unit_no),
            DETAIL  = 'pm.property unit numbers must be unique within a building (excluding deleted units).';
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION ngb_pm_property_trg_unit_no_unique()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM ngb_pm_property_check_unit_no_unique(NEW.catalog_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_cat_pm_property__unit_no_unique ON cat_pm_property;
CREATE CONSTRAINT TRIGGER trg_cat_pm_property__unit_no_unique
    AFTER INSERT OR UPDATE OF kind, parent_property_id, unit_no
    ON cat_pm_property
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_property_trg_unit_no_unique();

CREATE OR REPLACE FUNCTION ngb_pm_property_trg_catalog_restore_unit_no_unique()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.catalog_code = 'pm.property' AND OLD.is_deleted = TRUE AND NEW.is_deleted = FALSE THEN
        PERFORM ngb_pm_property_check_unit_no_unique(NEW.id);
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_catalogs__pm_property_restore_unit_no_unique ON catalogs;
CREATE CONSTRAINT TRIGGER trg_catalogs__pm_property_restore_unit_no_unique
    AFTER UPDATE OF is_deleted
    ON catalogs
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_property_trg_catalog_restore_unit_no_unique();

CREATE OR REPLACE FUNCTION ngb_pm_property_compute_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    parent_display text;
    line1 text;
    line2 text;
    city text;
    state text;
    zip text;
    addr text;
BEGIN
    IF TG_OP = 'UPDATE' THEN
        IF NEW.display IS NOT NULL AND OLD.display IS NOT NULL AND NEW.display <> OLD.display THEN
            RETURN NEW;
        END IF;
    ELSE
        IF NEW.display IS NOT NULL AND btrim(NEW.display) <> '' THEN
            RETURN NEW;
        END IF;
    END IF;

    IF NEW.kind = 'Building' THEN
        line1 := NULLIF(btrim(COALESCE(NEW.address_line1, '')), '');
        line2 := NULLIF(btrim(COALESCE(NEW.address_line2, '')), '');
        city  := NULLIF(btrim(COALESCE(NEW.city, '')), '');
        state := NULLIF(btrim(COALESCE(NEW.state, '')), '');
        zip   := NULLIF(btrim(COALESCE(NEW.zip, '')), '');

        addr := COALESCE(line1, '');

        IF line2 IS NOT NULL THEN
            addr := CASE WHEN addr = '' THEN line2 ELSE addr || ', ' || line2 END;
        END IF;

        IF city IS NOT NULL THEN
            addr := CASE WHEN addr = '' THEN city ELSE addr || ', ' || city END;
        END IF;

        IF state IS NOT NULL THEN
            addr := CASE WHEN addr = '' THEN state ELSE addr || ', ' || state END;
        END IF;

        IF zip IS NOT NULL THEN
            addr := CASE
                        WHEN addr = '' THEN zip
                        WHEN state IS NOT NULL THEN addr || ' ' || zip
                        ELSE addr || ', ' || zip
                    END;
        END IF;

        IF addr IS NULL OR btrim(addr) = '' THEN
            addr := '[Building]';
        END IF;

        NEW.display := addr;
        RETURN NEW;
    END IF;

    IF NEW.kind = 'Unit' THEN
        SELECT p.display
          INTO parent_display
          FROM cat_pm_property p
         WHERE p.catalog_id = NEW.parent_property_id;

        IF parent_display IS NULL OR btrim(parent_display) = '' THEN
            parent_display := '[Building]';
        END IF;

        IF NEW.unit_no IS NULL OR btrim(NEW.unit_no) = '' THEN
            NEW.display := parent_display || ' #?';
        ELSE
            NEW.display := parent_display || ' #' || btrim(NEW.unit_no);
        END IF;

        RETURN NEW;
    END IF;

    NEW.display := '[Property]';
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_cat_pm_property__compute_display ON cat_pm_property;
CREATE TRIGGER trg_cat_pm_property__compute_display
    BEFORE INSERT OR UPDATE
    ON cat_pm_property
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_property_compute_display();

CREATE OR REPLACE FUNCTION ngb_pm_property_cascade_building_display_to_units()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    parent_display text;
BEGIN
    IF NEW.kind <> 'Building' THEN
        RETURN NULL;
    END IF;

    IF COALESCE(NEW.display, '') = COALESCE(OLD.display, '') THEN
        RETURN NULL;
    END IF;

    parent_display := NULLIF(btrim(COALESCE(NEW.display, '')), '');
    IF parent_display IS NULL THEN
        parent_display := '[Building]';
    END IF;

    UPDATE cat_pm_property u
       SET display = parent_display || ' #' || COALESCE(NULLIF(btrim(u.unit_no), ''), '?')
     WHERE u.kind = 'Unit'
       AND u.parent_property_id = NEW.catalog_id;

    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS trg_cat_pm_property__cascade_building_display_to_units ON cat_pm_property;
CREATE TRIGGER trg_cat_pm_property__cascade_building_display_to_units
    AFTER UPDATE
    ON cat_pm_property
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_property_cascade_building_display_to_units();

-- -----------------------------------------------------------------------------
-- Lease invariants and display
-- -----------------------------------------------------------------------------

CREATE OR REPLACE FUNCTION ngb_pm_lease_compute_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    property_display text;
    start_text text;
    end_text text;
BEGIN
    SELECT p.display
      INTO property_display
      FROM cat_pm_property p
     WHERE p.catalog_id = NEW.property_id;

    IF property_display IS NULL OR btrim(property_display) = '' THEN
        property_display := '[Property]';
    END IF;

    IF NEW.start_on_utc IS NULL THEN
        start_text := '??/??/????';
    ELSE
        start_text := to_char(NEW.start_on_utc, 'MM/DD/YYYY');
    END IF;

    IF NEW.end_on_utc IS NULL THEN
        end_text := 'Open';
    ELSE
        end_text := to_char(NEW.end_on_utc, 'MM/DD/YYYY');
    END IF;

    NEW.display := property_display || ' — ' || start_text || ' → ' || end_text;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_lease__compute_display ON doc_pm_lease;
CREATE TRIGGER trg_doc_pm_lease__compute_display
    BEFORE INSERT OR UPDATE
    ON doc_pm_lease
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_lease_compute_display();

CREATE OR REPLACE FUNCTION public.ngb_pm_lease_assert_one_primary_party__by_lease()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    cnt integer;
BEGIN
    IF NOT EXISTS (
        SELECT 1
          FROM public.doc_pm_lease l
         WHERE l.document_id = NEW.document_id
    ) THEN
        RETURN NEW;
    END IF;

    SELECT COUNT(*)
      INTO cnt
      FROM public.doc_pm_lease__parties p
     WHERE p.document_id = NEW.document_id
       AND p.is_primary = TRUE;

    IF cnt <> 1 THEN
        RAISE EXCEPTION 'pm.lease must have exactly one primary party (document_id=%). Found %', NEW.document_id, cnt
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_lease__assert_one_primary_party ON public.doc_pm_lease;
CREATE CONSTRAINT TRIGGER trg_doc_pm_lease__assert_one_primary_party
    AFTER INSERT OR UPDATE
    ON public.doc_pm_lease
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW
EXECUTE FUNCTION public.ngb_pm_lease_assert_one_primary_party__by_lease();

CREATE OR REPLACE FUNCTION public.ngb_pm_lease_assert_one_primary_party__by_party_row()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    doc_id uuid;
    cnt integer;
BEGIN
    doc_id := COALESCE(NEW.document_id, OLD.document_id);

    IF NOT EXISTS (
        SELECT 1
          FROM public.doc_pm_lease l
         WHERE l.document_id = doc_id
    ) THEN
        RETURN NULL;
    END IF;

    SELECT COUNT(*)
      INTO cnt
      FROM public.doc_pm_lease__parties p
     WHERE p.document_id = doc_id
       AND p.is_primary = TRUE;

    IF cnt <> 1 THEN
        RAISE EXCEPTION 'pm.lease must have exactly one primary party (document_id=%). Found %', doc_id, cnt
            USING ERRCODE = '23514';
    END IF;

    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_lease__parties__assert_one_primary_party ON public.doc_pm_lease__parties;
CREATE CONSTRAINT TRIGGER trg_doc_pm_lease__parties__assert_one_primary_party
    AFTER INSERT OR UPDATE OR DELETE
    ON public.doc_pm_lease__parties
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW
EXECUTE FUNCTION public.ngb_pm_lease_assert_one_primary_party__by_party_row();

CREATE OR REPLACE FUNCTION ngb_pm_lease_assert_property_is_unit()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_kind text;
    v_is_deleted boolean;
BEGIN
    SELECT p.kind,
           c.is_deleted
      INTO v_kind,
           v_is_deleted
      FROM cat_pm_property p
      JOIN catalogs c ON c.id = p.catalog_id
     WHERE p.catalog_id = NEW.property_id;

    IF v_kind IS NULL THEN
        RAISE EXCEPTION 'pm.lease property_id must reference pm.property (document_id=%, property_id=%)', NEW.document_id, NEW.property_id
            USING ERRCODE = '23514';
    END IF;

    IF v_is_deleted THEN
        RAISE EXCEPTION 'pm.lease property is deleted (document_id=%, property_id=%)', NEW.document_id, NEW.property_id
            USING ERRCODE = '23514';
    END IF;

    IF NOT (lower(v_kind) = 'unit') THEN
        RAISE EXCEPTION 'pm.lease property must be Unit (document_id=%, property_id=%, kind=%)', NEW.document_id, NEW.property_id, v_kind
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_lease__assert_property_is_unit ON doc_pm_lease;
CREATE CONSTRAINT TRIGGER trg_doc_pm_lease__assert_property_is_unit
    AFTER INSERT OR UPDATE OF property_id
    ON doc_pm_lease
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_lease_assert_property_is_unit();

-- -----------------------------------------------------------------------------
-- Receivables invariants and computed display
-- -----------------------------------------------------------------------------



CREATE OR REPLACE FUNCTION ngb_pm_build_document_display(
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

CREATE OR REPLACE FUNCTION ngb_pm_compute_rent_charge_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_pm_build_document_display('Rent Charge', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_rent_charge__compute_display ON doc_pm_rent_charge;
CREATE TRIGGER trg_doc_pm_rent_charge__compute_display
BEFORE INSERT OR UPDATE ON doc_pm_rent_charge
FOR EACH ROW
EXECUTE FUNCTION ngb_pm_compute_rent_charge_display();

CREATE OR REPLACE FUNCTION ngb_pm_compute_receivable_charge_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_pm_build_document_display('Receivable Charge', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_receivable_charge__compute_display ON doc_pm_receivable_charge;
CREATE TRIGGER trg_doc_pm_receivable_charge__compute_display
BEFORE INSERT OR UPDATE ON doc_pm_receivable_charge
FOR EACH ROW
EXECUTE FUNCTION ngb_pm_compute_receivable_charge_display();

CREATE OR REPLACE FUNCTION ngb_pm_compute_receivable_payment_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_pm_build_document_display('Receivable Payment', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_receivable_payment__compute_display ON doc_pm_receivable_payment;
CREATE TRIGGER trg_doc_pm_receivable_payment__compute_display
BEFORE INSERT OR UPDATE ON doc_pm_receivable_payment
FOR EACH ROW
EXECUTE FUNCTION ngb_pm_compute_receivable_payment_display();

CREATE OR REPLACE FUNCTION ngb_pm_compute_receivable_apply_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_pm_build_document_display('Receivable Apply', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_receivable_apply__compute_display ON doc_pm_receivable_apply;
CREATE TRIGGER trg_doc_pm_receivable_apply__compute_display
BEFORE INSERT OR UPDATE ON doc_pm_receivable_apply
FOR EACH ROW
EXECUTE FUNCTION ngb_pm_compute_receivable_apply_display();

CREATE OR REPLACE FUNCTION ngb_pm_refresh_document_display_from_header()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    CASE NEW.type_code
        WHEN 'pm.rent_charge' THEN
            UPDATE doc_pm_rent_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_charge' THEN
            UPDATE doc_pm_receivable_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_payment' THEN
            UPDATE doc_pm_receivable_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_apply' THEN
            UPDATE doc_pm_receivable_apply
               SET display = display
             WHERE document_id = NEW.id;
    END CASE;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_documents__pm_refresh_typed_display ON documents;
CREATE TRIGGER trg_documents__pm_refresh_typed_display
AFTER UPDATE OF number, date_utc ON documents
FOR EACH ROW
WHEN (NEW.type_code IN ('pm.rent_charge', 'pm.receivable_charge', 'pm.receivable_payment', 'pm.receivable_apply'))
EXECUTE FUNCTION ngb_pm_refresh_document_display_from_header();


-- ============================================================================
-- Incorporated from historical migration: V2026_03_09_0001__ngb_pm_late_fee_charge.sql
-- ============================================================================

ALTER TABLE cat_pm_accounting_policy
    ADD COLUMN IF NOT EXISTS late_fee_income_account_id uuid NULL REFERENCES accounting_accounts(account_id);

CREATE INDEX IF NOT EXISTS ix_cat_pm_accounting_policy__late_fee_income_account_id
    ON cat_pm_accounting_policy(late_fee_income_account_id);

CREATE TABLE IF NOT EXISTS doc_pm_late_fee_charge (
    document_id    uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display        text NOT NULL,
    lease_id       uuid NOT NULL REFERENCES doc_pm_lease(document_id),
    due_on_utc     date NOT NULL,
    amount         numeric(18, 4) NOT NULL,
    memo           text NULL
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_late_fee_charge__display
    ON doc_pm_late_fee_charge(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_late_fee_charge__lease_id
    ON doc_pm_late_fee_charge(lease_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_late_fee_charge__due_on_utc
    ON doc_pm_late_fee_charge(due_on_utc);

CREATE INDEX IF NOT EXISTS ix_doc_pm_late_fee_charge__lease_due_document
    ON doc_pm_late_fee_charge(lease_id, due_on_utc, document_id);

CREATE OR REPLACE FUNCTION ngb_pm_receivable_apply_assert_document_refs()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_credit_type text;
    v_charge_type text;
BEGIN
    SELECT type_code INTO v_credit_type FROM documents WHERE id = NEW.credit_document_id;
    IF v_credit_type IS NULL OR v_credit_type NOT IN ('pm.receivable_payment', 'pm.receivable_credit_memo') THEN
        RAISE EXCEPTION 'pm.receivable_apply credit_document_id must reference pm.receivable_payment or pm.receivable_credit_memo (document_id=%, credit_document_id=%, type=%)', NEW.document_id, NEW.credit_document_id, v_credit_type
            USING ERRCODE = '23514';
    END IF;

    SELECT type_code INTO v_charge_type FROM documents WHERE id = NEW.charge_document_id;
    IF v_charge_type IS NULL OR v_charge_type NOT IN ('pm.receivable_charge', 'pm.late_fee_charge', 'pm.rent_charge') THEN
        RAISE EXCEPTION 'pm.receivable_apply charge_document_id must reference pm.receivable_charge, pm.late_fee_charge or pm.rent_charge (document_id=%, charge_document_id=%, type=%)', NEW.document_id, NEW.charge_document_id, v_charge_type
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_receivable_apply__assert_document_refs ON doc_pm_receivable_apply;
CREATE CONSTRAINT TRIGGER trg_doc_pm_receivable_apply__assert_document_refs
    AFTER INSERT OR UPDATE OF credit_document_id, charge_document_id
    ON doc_pm_receivable_apply
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_receivable_apply_assert_document_refs();

CREATE OR REPLACE FUNCTION ngb_pm_compute_late_fee_charge_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_pm_build_document_display('Late Fee Charge', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_late_fee_charge__compute_display ON doc_pm_late_fee_charge;
CREATE TRIGGER trg_doc_pm_late_fee_charge__compute_display
BEFORE INSERT OR UPDATE ON doc_pm_late_fee_charge
FOR EACH ROW
EXECUTE FUNCTION ngb_pm_compute_late_fee_charge_display();

CREATE OR REPLACE FUNCTION ngb_pm_refresh_document_display_from_header()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    CASE NEW.type_code
        WHEN 'pm.rent_charge' THEN
            UPDATE doc_pm_rent_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_charge' THEN
            UPDATE doc_pm_receivable_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.late_fee_charge' THEN
            UPDATE doc_pm_late_fee_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_payment' THEN
            UPDATE doc_pm_receivable_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_apply' THEN
            UPDATE doc_pm_receivable_apply
               SET display = display
             WHERE document_id = NEW.id;
    END CASE;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_documents__pm_refresh_typed_display ON documents;
CREATE TRIGGER trg_documents__pm_refresh_typed_display
AFTER UPDATE OF number, date_utc ON documents
FOR EACH ROW
WHEN (NEW.type_code IN ('pm.rent_charge', 'pm.receivable_charge', 'pm.late_fee_charge', 'pm.receivable_payment', 'pm.receivable_apply'))
EXECUTE FUNCTION ngb_pm_refresh_document_display_from_header();


-- ============================================================================
-- Incorporated from historical migration: V2026_03_10_0001__ngb_pm_rent_charge_applyable.sql
-- ============================================================================

CREATE OR REPLACE FUNCTION ngb_pm_receivable_apply_assert_document_refs()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_credit_type text;
    v_charge_type text;
BEGIN
    SELECT type_code INTO v_credit_type FROM documents WHERE id = NEW.credit_document_id;
    IF v_credit_type IS NULL OR v_credit_type NOT IN ('pm.receivable_payment', 'pm.receivable_credit_memo') THEN
        RAISE EXCEPTION 'pm.receivable_apply credit_document_id must reference pm.receivable_payment or pm.receivable_credit_memo (document_id=%, credit_document_id=%, type=%)', NEW.document_id, NEW.credit_document_id, v_credit_type
            USING ERRCODE = '23514';
    END IF;

    SELECT type_code INTO v_charge_type FROM documents WHERE id = NEW.charge_document_id;
    IF v_charge_type IS NULL OR v_charge_type NOT IN ('pm.receivable_charge', 'pm.late_fee_charge', 'pm.rent_charge') THEN
        RAISE EXCEPTION 'pm.receivable_apply charge_document_id must reference pm.receivable_charge, pm.late_fee_charge or pm.rent_charge (document_id=%, charge_document_id=%, type=%)', NEW.document_id, NEW.charge_document_id, v_charge_type
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;


-- ============================================================================
-- Incorporated from historical migration: V2026_03_10_0002__ngb_pm_receivable_returned_payment.sql
-- ============================================================================

CREATE TABLE IF NOT EXISTS doc_pm_receivable_returned_payment (
    document_id         uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display             text NOT NULL,
    original_payment_id uuid NOT NULL REFERENCES doc_pm_receivable_payment(document_id),
    returned_on_utc     date NOT NULL,
    amount              numeric(18, 4) NOT NULL,
    memo                text NULL
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_returned_payment__display
    ON doc_pm_receivable_returned_payment(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_returned_payment__original_payment_id
    ON doc_pm_receivable_returned_payment(original_payment_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_returned_payment__returned_on_utc
    ON doc_pm_receivable_returned_payment(returned_on_utc);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_returned_payment__original_payment_returned_document
    ON doc_pm_receivable_returned_payment(original_payment_id, returned_on_utc, document_id);


CREATE OR REPLACE FUNCTION ngb_pm_compute_receivable_returned_payment_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_pm_build_document_display('Receivable Returned Payment', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_receivable_returned_payment__compute_display ON doc_pm_receivable_returned_payment;
CREATE TRIGGER trg_doc_pm_receivable_returned_payment__compute_display
BEFORE INSERT OR UPDATE ON doc_pm_receivable_returned_payment
FOR EACH ROW
EXECUTE FUNCTION ngb_pm_compute_receivable_returned_payment_display();

CREATE OR REPLACE FUNCTION ngb_pm_refresh_document_display_from_header()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    CASE NEW.type_code
        WHEN 'pm.rent_charge' THEN
            UPDATE doc_pm_rent_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_charge' THEN
            UPDATE doc_pm_receivable_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.late_fee_charge' THEN
            UPDATE doc_pm_late_fee_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_payment' THEN
            UPDATE doc_pm_receivable_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_returned_payment' THEN
            UPDATE doc_pm_receivable_returned_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_apply' THEN
            UPDATE doc_pm_receivable_apply
               SET display = display
             WHERE document_id = NEW.id;
    END CASE;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_documents__pm_refresh_typed_display ON documents;
CREATE TRIGGER trg_documents__pm_refresh_typed_display
AFTER UPDATE OF number, date_utc ON documents
FOR EACH ROW
WHEN (NEW.type_code IN ('pm.rent_charge', 'pm.receivable_charge', 'pm.late_fee_charge', 'pm.receivable_payment', 'pm.receivable_returned_payment', 'pm.receivable_apply'))
EXECUTE FUNCTION ngb_pm_refresh_document_display_from_header();


-- ============================================================================
-- Incorporated from historical migration: V2026_03_10_0003__ngb_pm_receivable_credit_memo.sql
-- ============================================================================

CREATE TABLE IF NOT EXISTS doc_pm_receivable_credit_memo (
    document_id         uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display             text NOT NULL,
    lease_id            uuid NOT NULL REFERENCES doc_pm_lease(document_id),
    charge_type_id      uuid NOT NULL REFERENCES catalogs(id),
    credited_on_utc     date NOT NULL,
    amount              numeric(18, 4) NOT NULL,
    memo                text NULL
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_credit_memo__display
    ON doc_pm_receivable_credit_memo(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_credit_memo__lease_id
    ON doc_pm_receivable_credit_memo(lease_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_credit_memo__charge_type_id
    ON doc_pm_receivable_credit_memo(charge_type_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_credit_memo__credited_on_utc
    ON doc_pm_receivable_credit_memo(credited_on_utc);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_credit_memo__lease_credited_document
    ON doc_pm_receivable_credit_memo(lease_id, credited_on_utc, document_id);



CREATE OR REPLACE FUNCTION ngb_pm_compute_receivable_credit_memo_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_pm_build_document_display('Receivable Credit Memo', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_receivable_credit_memo__compute_display ON doc_pm_receivable_credit_memo;
CREATE TRIGGER trg_doc_pm_receivable_credit_memo__compute_display
BEFORE INSERT OR UPDATE ON doc_pm_receivable_credit_memo
FOR EACH ROW
EXECUTE FUNCTION ngb_pm_compute_receivable_credit_memo_display();

CREATE OR REPLACE FUNCTION ngb_pm_refresh_document_display_from_header()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    CASE NEW.type_code
        WHEN 'pm.rent_charge' THEN
            UPDATE doc_pm_rent_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_charge' THEN
            UPDATE doc_pm_receivable_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.late_fee_charge' THEN
            UPDATE doc_pm_late_fee_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_payment' THEN
            UPDATE doc_pm_receivable_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_returned_payment' THEN
            UPDATE doc_pm_receivable_returned_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_credit_memo' THEN
            UPDATE doc_pm_receivable_credit_memo
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_apply' THEN
            UPDATE doc_pm_receivable_apply
               SET display = display
             WHERE document_id = NEW.id;
    END CASE;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_documents__pm_refresh_typed_display ON documents;
CREATE TRIGGER trg_documents__pm_refresh_typed_display
AFTER UPDATE OF number, date_utc ON documents
FOR EACH ROW
WHEN (NEW.type_code IN ('pm.rent_charge', 'pm.receivable_charge', 'pm.late_fee_charge', 'pm.receivable_payment', 'pm.receivable_returned_payment', 'pm.receivable_credit_memo', 'pm.receivable_apply'))
EXECUTE FUNCTION ngb_pm_refresh_document_display_from_header();


-- ============================================================================
-- Incorporated from historical migration: V2026_03_10_0004__ngb_pm_bank_accounts.sql
-- ============================================================================

CREATE TABLE IF NOT EXISTS cat_pm_bank_account (
    catalog_id      uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display         text NOT NULL,
    bank_name       text NOT NULL,
    account_name    text NOT NULL,
    last4           text NOT NULL,
    gl_account_id   uuid NOT NULL REFERENCES accounting_accounts(account_id),
    is_default      boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT ck_cat_pm_bank_account__last4_digits CHECK (last4 ~ '^[0-9]{4}$')
);

CREATE INDEX IF NOT EXISTS ix_cat_pm_bank_account__display
    ON cat_pm_bank_account(display);

CREATE INDEX IF NOT EXISTS ix_cat_pm_bank_account__bank_name
    ON cat_pm_bank_account(bank_name);

CREATE INDEX IF NOT EXISTS ix_cat_pm_bank_account__gl_account_id
    ON cat_pm_bank_account(gl_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_pm_bank_account__is_default
    ON cat_pm_bank_account(is_default);

ALTER TABLE doc_pm_receivable_payment
    ADD COLUMN IF NOT EXISTS bank_account_id uuid NULL REFERENCES catalogs(id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_receivable_payment__bank_account_id
    ON doc_pm_receivable_payment(bank_account_id);


CREATE OR REPLACE FUNCTION ngb_pm_assert_bank_account_ref_is_active_pm_bank_account(p_bank_account_id uuid, p_document_id uuid, p_document_type text)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_catalog_code text;
    v_is_deleted boolean;
BEGIN
    IF p_bank_account_id IS NULL THEN
        RETURN;
    END IF;

    SELECT c.catalog_code,
           c.is_deleted
      INTO v_catalog_code,
           v_is_deleted
      FROM catalogs c
     WHERE c.id = p_bank_account_id;

    IF v_catalog_code IS NULL OR v_catalog_code <> 'pm.bank_account' THEN
        RAISE EXCEPTION '% bank_account_id must reference pm.bank_account (document_id=%, bank_account_id=%, catalog_code=%)',
            p_document_type, p_document_id, p_bank_account_id, v_catalog_code
            USING ERRCODE = '23514';
    END IF;

    IF v_is_deleted THEN
        RAISE EXCEPTION '% bank_account is deleted (document_id=%, bank_account_id=%)',
            p_document_type, p_document_id, p_bank_account_id
            USING ERRCODE = '23514';
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION ngb_pm_receivable_payment_assert_bank_account()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM ngb_pm_assert_bank_account_ref_is_active_pm_bank_account(NEW.bank_account_id, NEW.document_id, 'pm.receivable_payment');
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_receivable_payment__assert_bank_account ON doc_pm_receivable_payment;
CREATE CONSTRAINT TRIGGER trg_doc_pm_receivable_payment__assert_bank_account
    AFTER INSERT OR UPDATE OF bank_account_id
    ON doc_pm_receivable_payment
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_receivable_payment_assert_bank_account();



-- ============================================================================
-- Incorporated from historical migration: V2026_03_10_0005__ngb_pm_bank_account_display.sql
-- ============================================================================

CREATE OR REPLACE FUNCTION ngb_pm_bank_account_compute_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := concat_ws(' ',
        NULLIF(btrim(COALESCE(NEW.bank_name, '')), ''),
        NULLIF(btrim(COALESCE(NEW.account_name, '')), ''),
        CASE
            WHEN NULLIF(btrim(COALESCE(NEW.last4, '')), '') IS NULL THEN NULL
            ELSE '**** ' || btrim(NEW.last4)
        END);

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_cat_pm_bank_account__compute_display ON cat_pm_bank_account;
CREATE TRIGGER trg_cat_pm_bank_account__compute_display
    BEFORE INSERT OR UPDATE OF bank_name, account_name, last4
    ON cat_pm_bank_account
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_bank_account_compute_display();

UPDATE cat_pm_bank_account
   SET display = concat_ws(' ',
       NULLIF(btrim(COALESCE(bank_name, '')), ''),
       NULLIF(btrim(COALESCE(account_name, '')), ''),
       CASE
           WHEN NULLIF(btrim(COALESCE(last4, '')), '') IS NULL THEN NULL
           ELSE '**** ' || btrim(last4)
       END);


-- ============================================================================
-- Incorporated from historical migration: V2026_03_11_0001__ngb_pm_party_roles.sql
-- ============================================================================

ALTER TABLE cat_pm_party
    ADD COLUMN IF NOT EXISTS is_tenant boolean,
    ADD COLUMN IF NOT EXISTS is_vendor boolean;

UPDATE cat_pm_party
SET is_tenant = true
WHERE is_tenant IS NULL;

UPDATE cat_pm_party
SET is_vendor = false
WHERE is_vendor IS NULL;

ALTER TABLE cat_pm_party
    ALTER COLUMN is_tenant SET DEFAULT true,
    ALTER COLUMN is_vendor SET DEFAULT false,
    ALTER COLUMN is_tenant SET NOT NULL,
    ALTER COLUMN is_vendor SET NOT NULL;

CREATE INDEX IF NOT EXISTS ix_cat_pm_party__is_tenant
    ON cat_pm_party(is_tenant);

CREATE INDEX IF NOT EXISTS ix_cat_pm_party__is_vendor
    ON cat_pm_party(is_vendor);


-- ============================================================================
-- Incorporated from historical migration: V2026_03_11_0003__ngb_pm_payable_charge.sql
-- ============================================================================

ALTER TABLE cat_pm_accounting_policy
    ADD COLUMN IF NOT EXISTS ap_vendors_account_id uuid NULL REFERENCES accounting_accounts(account_id),
    ADD COLUMN IF NOT EXISTS payables_open_items_register_id uuid NULL REFERENCES operational_registers(register_id);

CREATE INDEX IF NOT EXISTS ix_cat_pm_accounting_policy__ap_vendors_account_id
    ON cat_pm_accounting_policy(ap_vendors_account_id);

CREATE INDEX IF NOT EXISTS ix_cat_pm_accounting_policy__payables_open_items_register_id
    ON cat_pm_accounting_policy(payables_open_items_register_id);

CREATE TABLE IF NOT EXISTS cat_pm_payable_charge_type (
    catalog_id        uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display           text NOT NULL,
    debit_account_id  uuid NULL REFERENCES accounting_accounts(account_id)
);

CREATE INDEX IF NOT EXISTS ix_cat_pm_payable_charge_type__display
    ON cat_pm_payable_charge_type(display);

CREATE INDEX IF NOT EXISTS ix_cat_pm_payable_charge_type__debit_account_id
    ON cat_pm_payable_charge_type(debit_account_id);

CREATE TABLE IF NOT EXISTS doc_pm_payable_charge (
    document_id         uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display             text NOT NULL,
    party_id            uuid NOT NULL REFERENCES catalogs(id),
    property_id         uuid NOT NULL REFERENCES catalogs(id),
    charge_type_id      uuid NOT NULL REFERENCES catalogs(id),
    vendor_invoice_no   text NULL,
    due_on_utc          date NOT NULL,
    amount              numeric(18, 4) NOT NULL,
    memo                text NULL
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_charge__display
    ON doc_pm_payable_charge(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_charge__party_id
    ON doc_pm_payable_charge(party_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_charge__property_id
    ON doc_pm_payable_charge(property_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_charge__charge_type_id
    ON doc_pm_payable_charge(charge_type_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_charge__due_on_utc
    ON doc_pm_payable_charge(due_on_utc);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_charge__vendor_invoice_no
    ON doc_pm_payable_charge(vendor_invoice_no);

CREATE OR REPLACE FUNCTION ngb_pm_payable_charge_assert_vendor_party()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_is_deleted boolean;
    v_is_vendor boolean;
BEGIN
    SELECT c.is_deleted,
           COALESCE(p.is_vendor, FALSE)
      INTO v_is_deleted,
           v_is_vendor
      FROM cat_pm_party p
      JOIN catalogs c ON c.id = p.catalog_id
     WHERE p.catalog_id = NEW.party_id;

    IF v_is_deleted IS NULL THEN
        RAISE EXCEPTION 'pm.payable_charge party_id must reference pm.party (document_id=%, party_id=%)', NEW.document_id, NEW.party_id
            USING ERRCODE = '23514';
    END IF;

    IF v_is_deleted THEN
        RAISE EXCEPTION 'pm.payable_charge vendor is deleted (document_id=%, party_id=%)', NEW.document_id, NEW.party_id
            USING ERRCODE = '23514';
    END IF;

    IF NOT v_is_vendor THEN
        RAISE EXCEPTION 'pm.payable_charge vendor must have is_vendor=true (document_id=%, party_id=%)', NEW.document_id, NEW.party_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_charge__assert_vendor_party ON doc_pm_payable_charge;
CREATE CONSTRAINT TRIGGER trg_doc_pm_payable_charge__assert_vendor_party
    AFTER INSERT OR UPDATE OF party_id
    ON doc_pm_payable_charge
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_payable_charge_assert_vendor_party();

CREATE OR REPLACE FUNCTION ngb_pm_payable_charge_assert_property_active()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_is_deleted boolean;
BEGIN
    SELECT c.is_deleted
      INTO v_is_deleted
      FROM cat_pm_property p
      JOIN catalogs c ON c.id = p.catalog_id
     WHERE p.catalog_id = NEW.property_id;

    IF v_is_deleted IS NULL THEN
        RAISE EXCEPTION 'pm.payable_charge property_id must reference pm.property (document_id=%, property_id=%)', NEW.document_id, NEW.property_id
            USING ERRCODE = '23514';
    END IF;

    IF v_is_deleted THEN
        RAISE EXCEPTION 'pm.payable_charge property is deleted (document_id=%, property_id=%)', NEW.document_id, NEW.property_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_charge__assert_property_active ON doc_pm_payable_charge;
CREATE CONSTRAINT TRIGGER trg_doc_pm_payable_charge__assert_property_active
    AFTER INSERT OR UPDATE OF property_id
    ON doc_pm_payable_charge
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_payable_charge_assert_property_active();

CREATE OR REPLACE FUNCTION ngb_pm_payable_charge_assert_charge_type_active()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_is_deleted boolean;
    v_catalog_code text;
BEGIN
    SELECT c.is_deleted,
           c.catalog_code
      INTO v_is_deleted,
           v_catalog_code
      FROM catalogs c
     WHERE c.id = NEW.charge_type_id;

    IF v_catalog_code IS NULL OR v_catalog_code <> 'pm.payable_charge_type' THEN
        RAISE EXCEPTION 'pm.payable_charge charge_type_id must reference pm.payable_charge_type (document_id=%, charge_type_id=%, catalog_code=%)', NEW.document_id, NEW.charge_type_id, v_catalog_code
            USING ERRCODE = '23514';
    END IF;

    IF v_is_deleted THEN
        RAISE EXCEPTION 'pm.payable_charge charge type is deleted (document_id=%, charge_type_id=%)', NEW.document_id, NEW.charge_type_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_charge__assert_charge_type_active ON doc_pm_payable_charge;
CREATE CONSTRAINT TRIGGER trg_doc_pm_payable_charge__assert_charge_type_active
    AFTER INSERT OR UPDATE OF charge_type_id
    ON doc_pm_payable_charge
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_payable_charge_assert_charge_type_active();

CREATE OR REPLACE FUNCTION ngb_pm_compute_payable_charge_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_pm_build_document_display('Payable Charge', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_charge__compute_display ON doc_pm_payable_charge;
CREATE TRIGGER trg_doc_pm_payable_charge__compute_display
BEFORE INSERT OR UPDATE ON doc_pm_payable_charge
FOR EACH ROW
EXECUTE FUNCTION ngb_pm_compute_payable_charge_display();

CREATE OR REPLACE FUNCTION ngb_pm_refresh_document_display_from_header()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    CASE NEW.type_code
        WHEN 'pm.rent_charge' THEN
            UPDATE doc_pm_rent_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_charge' THEN
            UPDATE doc_pm_receivable_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.late_fee_charge' THEN
            UPDATE doc_pm_late_fee_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_payment' THEN
            UPDATE doc_pm_receivable_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_returned_payment' THEN
            UPDATE doc_pm_receivable_returned_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_credit_memo' THEN
            UPDATE doc_pm_receivable_credit_memo
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.payable_charge' THEN
            UPDATE doc_pm_payable_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_apply' THEN
            UPDATE doc_pm_receivable_apply
               SET display = display
             WHERE document_id = NEW.id;
    END CASE;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_documents__pm_refresh_typed_display ON documents;
CREATE TRIGGER trg_documents__pm_refresh_typed_display
AFTER UPDATE OF number, date_utc ON documents
FOR EACH ROW
WHEN (NEW.type_code IN ('pm.rent_charge', 'pm.receivable_charge', 'pm.late_fee_charge', 'pm.receivable_payment', 'pm.receivable_returned_payment', 'pm.receivable_credit_memo', 'pm.payable_charge', 'pm.receivable_apply'))
EXECUTE FUNCTION ngb_pm_refresh_document_display_from_header();


-- ============================================================================
-- Incorporated from historical migration: V2026_03_11_0004__ngb_pm_payable_payment.sql
-- ============================================================================

CREATE TABLE IF NOT EXISTS doc_pm_payable_payment (
    document_id     uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display         text NOT NULL,
    party_id        uuid NOT NULL REFERENCES catalogs(id),
    property_id     uuid NOT NULL REFERENCES catalogs(id),
    bank_account_id uuid NULL REFERENCES catalogs(id),
    paid_on_utc     date NOT NULL,
    amount          numeric(18, 4) NOT NULL,
    memo            text NULL
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_payment__display
    ON doc_pm_payable_payment(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_payment__party_id
    ON doc_pm_payable_payment(party_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_payment__property_id
    ON doc_pm_payable_payment(property_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_payment__bank_account_id
    ON doc_pm_payable_payment(bank_account_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_payment__paid_on_utc
    ON doc_pm_payable_payment(paid_on_utc);

CREATE OR REPLACE FUNCTION ngb_pm_payable_payment_assert_vendor_party()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_is_deleted boolean;
    v_is_vendor boolean;
BEGIN
    SELECT c.is_deleted,
           COALESCE(p.is_vendor, FALSE)
      INTO v_is_deleted,
           v_is_vendor
      FROM cat_pm_party p
      JOIN catalogs c ON c.id = p.catalog_id
     WHERE p.catalog_id = NEW.party_id;

    IF v_is_deleted IS NULL THEN
        RAISE EXCEPTION 'pm.payable_payment party_id must reference pm.party (document_id=%, party_id=%)', NEW.document_id, NEW.party_id
            USING ERRCODE = '23514';
    END IF;

    IF v_is_deleted THEN
        RAISE EXCEPTION 'pm.payable_payment vendor is deleted (document_id=%, party_id=%)', NEW.document_id, NEW.party_id
            USING ERRCODE = '23514';
    END IF;

    IF NOT v_is_vendor THEN
        RAISE EXCEPTION 'pm.payable_payment vendor must have is_vendor=true (document_id=%, party_id=%)', NEW.document_id, NEW.party_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_payment__assert_vendor_party ON doc_pm_payable_payment;
CREATE CONSTRAINT TRIGGER trg_doc_pm_payable_payment__assert_vendor_party
    AFTER INSERT OR UPDATE OF party_id
    ON doc_pm_payable_payment
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_payable_payment_assert_vendor_party();

CREATE OR REPLACE FUNCTION ngb_pm_payable_payment_assert_property_active()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_is_deleted boolean;
BEGIN
    SELECT c.is_deleted
      INTO v_is_deleted
      FROM cat_pm_property p
      JOIN catalogs c ON c.id = p.catalog_id
     WHERE p.catalog_id = NEW.property_id;

    IF v_is_deleted IS NULL THEN
        RAISE EXCEPTION 'pm.payable_payment property_id must reference pm.property (document_id=%, property_id=%)', NEW.document_id, NEW.property_id
            USING ERRCODE = '23514';
    END IF;

    IF v_is_deleted THEN
        RAISE EXCEPTION 'pm.payable_payment property is deleted (document_id=%, property_id=%)', NEW.document_id, NEW.property_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_payment__assert_property_active ON doc_pm_payable_payment;
CREATE CONSTRAINT TRIGGER trg_doc_pm_payable_payment__assert_property_active
    AFTER INSERT OR UPDATE OF property_id
    ON doc_pm_payable_payment
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_payable_payment_assert_property_active();

CREATE OR REPLACE FUNCTION ngb_pm_payable_payment_assert_bank_account()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM ngb_pm_assert_bank_account_ref_is_active_pm_bank_account(NEW.bank_account_id, NEW.document_id, 'pm.payable_payment');
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_payment__assert_bank_account ON doc_pm_payable_payment;
CREATE CONSTRAINT TRIGGER trg_doc_pm_payable_payment__assert_bank_account
    AFTER INSERT OR UPDATE OF bank_account_id
    ON doc_pm_payable_payment
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_payable_payment_assert_bank_account();

CREATE OR REPLACE FUNCTION ngb_pm_compute_payable_payment_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_pm_build_document_display('Payable Payment', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_payment__compute_display ON doc_pm_payable_payment;
CREATE TRIGGER trg_doc_pm_payable_payment__compute_display
BEFORE INSERT OR UPDATE ON doc_pm_payable_payment
FOR EACH ROW
EXECUTE FUNCTION ngb_pm_compute_payable_payment_display();

CREATE OR REPLACE FUNCTION ngb_pm_refresh_document_display_from_header()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    CASE NEW.type_code
        WHEN 'pm.rent_charge' THEN
            UPDATE doc_pm_rent_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_charge' THEN
            UPDATE doc_pm_receivable_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.late_fee_charge' THEN
            UPDATE doc_pm_late_fee_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_payment' THEN
            UPDATE doc_pm_receivable_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_returned_payment' THEN
            UPDATE doc_pm_receivable_returned_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_credit_memo' THEN
            UPDATE doc_pm_receivable_credit_memo
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.payable_charge' THEN
            UPDATE doc_pm_payable_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.payable_payment' THEN
            UPDATE doc_pm_payable_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_apply' THEN
            UPDATE doc_pm_receivable_apply
               SET display = display
             WHERE document_id = NEW.id;
    END CASE;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_documents__pm_refresh_typed_display ON documents;
CREATE TRIGGER trg_documents__pm_refresh_typed_display
AFTER UPDATE OF number, date_utc ON documents
FOR EACH ROW
WHEN (NEW.type_code IN ('pm.rent_charge', 'pm.receivable_charge', 'pm.late_fee_charge', 'pm.receivable_payment', 'pm.receivable_returned_payment', 'pm.receivable_credit_memo', 'pm.payable_charge', 'pm.payable_payment', 'pm.receivable_apply'))
EXECUTE FUNCTION ngb_pm_refresh_document_display_from_header();


-- ============================================================================
-- Incorporated from historical migration: V2026_03_11_0005__ngb_pm_payable_apply.sql
-- ============================================================================
CREATE TABLE IF NOT EXISTS doc_pm_payable_apply (
    document_id        uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display            text NOT NULL,
    credit_document_id uuid NOT NULL REFERENCES documents(id),
    charge_document_id uuid NOT NULL REFERENCES documents(id),
    applied_on_utc     date NOT NULL,
    amount             numeric(18, 4) NOT NULL,
    memo               text NULL
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_apply__display
    ON doc_pm_payable_apply(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_apply__credit_document_id
    ON doc_pm_payable_apply(credit_document_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_apply__charge_document_id
    ON doc_pm_payable_apply(charge_document_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_apply__applied_on_utc
    ON doc_pm_payable_apply(applied_on_utc);

CREATE OR REPLACE FUNCTION ngb_pm_compute_payable_apply_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_pm_build_document_display('Payable Apply', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_apply__compute_display ON doc_pm_payable_apply;
CREATE TRIGGER trg_doc_pm_payable_apply__compute_display
BEFORE INSERT OR UPDATE ON doc_pm_payable_apply
FOR EACH ROW
EXECUTE FUNCTION ngb_pm_compute_payable_apply_display();

CREATE OR REPLACE FUNCTION ngb_pm_payable_apply_assert_document_refs()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_credit_type text;
    v_charge_type text;
BEGIN
    SELECT type_code INTO v_credit_type FROM documents WHERE id = NEW.credit_document_id;
    IF v_credit_type IS NULL OR v_credit_type NOT IN ('pm.payable_payment', 'pm.payable_credit_memo') THEN
        RAISE EXCEPTION 'pm.payable_apply credit_document_id must reference pm.payable_payment or pm.payable_credit_memo (document_id=%, credit_document_id=%, type=%)', NEW.document_id, NEW.credit_document_id, v_credit_type
            USING ERRCODE = '23514';
    END IF;

    SELECT type_code INTO v_charge_type FROM documents WHERE id = NEW.charge_document_id;
    IF v_charge_type IS NULL OR v_charge_type <> 'pm.payable_charge' THEN
        RAISE EXCEPTION 'pm.payable_apply charge_document_id must reference pm.payable_charge (document_id=%, charge_document_id=%, type=%)', NEW.document_id, NEW.charge_document_id, v_charge_type
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_apply__assert_document_refs ON doc_pm_payable_apply;
CREATE CONSTRAINT TRIGGER trg_doc_pm_payable_apply__assert_document_refs
    AFTER INSERT OR UPDATE OF credit_document_id, charge_document_id
    ON doc_pm_payable_apply
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_payable_apply_assert_document_refs();

CREATE OR REPLACE FUNCTION ngb_pm_refresh_document_display_from_header()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    CASE NEW.type_code
        WHEN 'pm.rent_charge' THEN
            UPDATE doc_pm_rent_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_charge' THEN
            UPDATE doc_pm_receivable_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.late_fee_charge' THEN
            UPDATE doc_pm_late_fee_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_payment' THEN
            UPDATE doc_pm_receivable_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_returned_payment' THEN
            UPDATE doc_pm_receivable_returned_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_credit_memo' THEN
            UPDATE doc_pm_receivable_credit_memo
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.payable_charge' THEN
            UPDATE doc_pm_payable_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.payable_payment' THEN
            UPDATE doc_pm_payable_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_apply' THEN
            UPDATE doc_pm_receivable_apply
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.payable_apply' THEN
            UPDATE doc_pm_payable_apply
               SET display = display
             WHERE document_id = NEW.id;
    END CASE;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_documents__pm_refresh_typed_display ON documents;
CREATE TRIGGER trg_documents__pm_refresh_typed_display
AFTER UPDATE OF number, date_utc ON documents
FOR EACH ROW
WHEN (NEW.type_code IN ('pm.rent_charge', 'pm.receivable_charge', 'pm.late_fee_charge', 'pm.receivable_payment', 'pm.receivable_returned_payment', 'pm.receivable_credit_memo', 'pm.payable_charge', 'pm.payable_payment', 'pm.receivable_apply', 'pm.payable_apply'))
EXECUTE FUNCTION ngb_pm_refresh_document_display_from_header();


-- ============================================================================
-- Receivable Apply generalized credit-source model (clean-slate baseline form)
-- ============================================================================

CREATE OR REPLACE FUNCTION ngb_pm_receivable_apply_assert_document_refs()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_credit_type text;
    v_charge_type text;
BEGIN
    SELECT type_code INTO v_credit_type FROM documents WHERE id = NEW.credit_document_id;
    IF v_credit_type IS NULL OR v_credit_type NOT IN ('pm.receivable_payment', 'pm.receivable_credit_memo') THEN
        RAISE EXCEPTION 'pm.receivable_apply credit_document_id must reference pm.receivable_payment or pm.receivable_credit_memo (document_id=%, credit_document_id=%, type=%)', NEW.document_id, NEW.credit_document_id, v_credit_type
            USING ERRCODE = '23514';
    END IF;

    SELECT type_code INTO v_charge_type FROM documents WHERE id = NEW.charge_document_id;
    IF v_charge_type IS NULL OR v_charge_type NOT IN ('pm.receivable_charge', 'pm.late_fee_charge', 'pm.rent_charge') THEN
        RAISE EXCEPTION 'pm.receivable_apply charge_document_id must reference pm.receivable_charge, pm.late_fee_charge or pm.rent_charge (document_id=%, charge_document_id=%, type=%)', NEW.document_id, NEW.charge_document_id, v_charge_type
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_receivable_apply__assert_document_refs ON doc_pm_receivable_apply;
CREATE CONSTRAINT TRIGGER trg_doc_pm_receivable_apply__assert_document_refs
    AFTER INSERT OR UPDATE OF credit_document_id, charge_document_id
    ON doc_pm_receivable_apply
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_receivable_apply_assert_document_refs();


-- ============================================================================
-- Incorporated from historical migration: V2026_03_13_0001__ngb_pm_payable_credit_memo.sql
-- ============================================================================

CREATE TABLE IF NOT EXISTS doc_pm_payable_credit_memo (
    document_id         uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display             text NOT NULL,
    party_id            uuid NOT NULL REFERENCES catalogs(id),
    property_id         uuid NOT NULL REFERENCES catalogs(id),
    charge_type_id      uuid NOT NULL REFERENCES catalogs(id),
    credited_on_utc     date NOT NULL,
    amount              numeric(18, 4) NOT NULL,
    memo                text NULL
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_credit_memo__display
    ON doc_pm_payable_credit_memo(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_credit_memo__party_id
    ON doc_pm_payable_credit_memo(party_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_credit_memo__property_id
    ON doc_pm_payable_credit_memo(property_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_credit_memo__charge_type_id
    ON doc_pm_payable_credit_memo(charge_type_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_payable_credit_memo__credited_on_utc
    ON doc_pm_payable_credit_memo(credited_on_utc);

CREATE OR REPLACE FUNCTION ngb_pm_payable_credit_memo_assert_vendor_party()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_is_deleted boolean;
    v_is_vendor boolean;
BEGIN
    SELECT c.is_deleted,
           COALESCE(p.is_vendor, FALSE)
      INTO v_is_deleted,
           v_is_vendor
      FROM cat_pm_party p
      JOIN catalogs c ON c.id = p.catalog_id
     WHERE p.catalog_id = NEW.party_id;

    IF v_is_deleted IS NULL THEN
        RAISE EXCEPTION 'pm.payable_credit_memo party_id must reference pm.party (document_id=%, party_id=%)', NEW.document_id, NEW.party_id
            USING ERRCODE = '23514';
    END IF;

    IF v_is_deleted THEN
        RAISE EXCEPTION 'pm.payable_credit_memo vendor is deleted (document_id=%, party_id=%)', NEW.document_id, NEW.party_id
            USING ERRCODE = '23514';
    END IF;

    IF NOT v_is_vendor THEN
        RAISE EXCEPTION 'pm.payable_credit_memo vendor must have is_vendor=true (document_id=%, party_id=%)', NEW.document_id, NEW.party_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_credit_memo__assert_vendor_party ON doc_pm_payable_credit_memo;
CREATE CONSTRAINT TRIGGER trg_doc_pm_payable_credit_memo__assert_vendor_party
    AFTER INSERT OR UPDATE OF party_id
    ON doc_pm_payable_credit_memo
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_payable_credit_memo_assert_vendor_party();

CREATE OR REPLACE FUNCTION ngb_pm_payable_credit_memo_assert_property_active()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_is_deleted boolean;
BEGIN
    SELECT c.is_deleted
      INTO v_is_deleted
      FROM cat_pm_property p
      JOIN catalogs c ON c.id = p.catalog_id
     WHERE p.catalog_id = NEW.property_id;

    IF v_is_deleted IS NULL THEN
        RAISE EXCEPTION 'pm.payable_credit_memo property_id must reference pm.property (document_id=%, property_id=%)', NEW.document_id, NEW.property_id
            USING ERRCODE = '23514';
    END IF;

    IF v_is_deleted THEN
        RAISE EXCEPTION 'pm.payable_credit_memo property is deleted (document_id=%, property_id=%)', NEW.document_id, NEW.property_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_credit_memo__assert_property_active ON doc_pm_payable_credit_memo;
CREATE CONSTRAINT TRIGGER trg_doc_pm_payable_credit_memo__assert_property_active
    AFTER INSERT OR UPDATE OF property_id
    ON doc_pm_payable_credit_memo
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_payable_credit_memo_assert_property_active();

CREATE OR REPLACE FUNCTION ngb_pm_payable_credit_memo_assert_charge_type_active()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_catalog_code text;
    v_is_deleted boolean;
BEGIN
    SELECT c.catalog_code, c.is_deleted
      INTO v_catalog_code, v_is_deleted
      FROM catalogs c
     WHERE c.id = NEW.charge_type_id;

    IF v_catalog_code IS NULL OR v_catalog_code <> 'pm.payable_charge_type' THEN
        RAISE EXCEPTION 'pm.payable_credit_memo charge_type_id must reference pm.payable_charge_type (document_id=%, charge_type_id=%, catalog_code=%)', NEW.document_id, NEW.charge_type_id, v_catalog_code
            USING ERRCODE = '23514';
    END IF;

    IF v_is_deleted THEN
        RAISE EXCEPTION 'pm.payable_credit_memo charge_type is deleted (document_id=%, charge_type_id=%)', NEW.document_id, NEW.charge_type_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_credit_memo__assert_charge_type_active ON doc_pm_payable_credit_memo;
CREATE CONSTRAINT TRIGGER trg_doc_pm_payable_credit_memo__assert_charge_type_active
    AFTER INSERT OR UPDATE OF charge_type_id
    ON doc_pm_payable_credit_memo
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_payable_credit_memo_assert_charge_type_active();


CREATE OR REPLACE FUNCTION ngb_pm_compute_payable_credit_memo_display()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.display := ngb_pm_build_document_display('Payable Credit Memo', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_credit_memo__compute_display ON doc_pm_payable_credit_memo;
CREATE TRIGGER trg_doc_pm_payable_credit_memo__compute_display
BEFORE INSERT OR UPDATE ON doc_pm_payable_credit_memo
FOR EACH ROW
EXECUTE FUNCTION ngb_pm_compute_payable_credit_memo_display();

CREATE OR REPLACE FUNCTION ngb_pm_refresh_document_display_from_header()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    CASE NEW.type_code
        WHEN 'pm.rent_charge' THEN
            UPDATE doc_pm_rent_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_charge' THEN
            UPDATE doc_pm_receivable_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.late_fee_charge' THEN
            UPDATE doc_pm_late_fee_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_payment' THEN
            UPDATE doc_pm_receivable_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_returned_payment' THEN
            UPDATE doc_pm_receivable_returned_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_credit_memo' THEN
            UPDATE doc_pm_receivable_credit_memo
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.payable_charge' THEN
            UPDATE doc_pm_payable_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.payable_payment' THEN
            UPDATE doc_pm_payable_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.payable_credit_memo' THEN
            UPDATE doc_pm_payable_credit_memo
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_apply' THEN
            UPDATE doc_pm_receivable_apply
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.payable_apply' THEN
            UPDATE doc_pm_payable_apply
               SET display = display
             WHERE document_id = NEW.id;
    END CASE;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_documents__pm_refresh_typed_display ON documents;
CREATE TRIGGER trg_documents__pm_refresh_typed_display
AFTER UPDATE OF number, date_utc ON documents
FOR EACH ROW
WHEN (NEW.type_code IN ('pm.rent_charge', 'pm.receivable_charge', 'pm.late_fee_charge', 'pm.receivable_payment', 'pm.receivable_returned_payment', 'pm.receivable_credit_memo', 'pm.payable_charge', 'pm.payable_payment', 'pm.payable_credit_memo', 'pm.receivable_apply', 'pm.payable_apply'))
EXECUTE FUNCTION ngb_pm_refresh_document_display_from_header();


-- ============================================================================
-- Payable Apply generalized credit-source model (clean-slate baseline form)
-- ============================================================================

CREATE OR REPLACE FUNCTION ngb_pm_payable_apply_assert_document_refs()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_credit_type text;
    v_charge_type text;
BEGIN
    SELECT type_code INTO v_credit_type FROM documents WHERE id = NEW.credit_document_id;
    IF v_credit_type IS NULL OR v_credit_type NOT IN ('pm.payable_payment', 'pm.payable_credit_memo') THEN
        RAISE EXCEPTION 'pm.payable_apply credit_document_id must reference pm.payable_payment or pm.payable_credit_memo (document_id=%, credit_document_id=%, type=%)', NEW.document_id, NEW.credit_document_id, v_credit_type
            USING ERRCODE = '23514';
    END IF;

    SELECT type_code INTO v_charge_type FROM documents WHERE id = NEW.charge_document_id;
    IF v_charge_type IS NULL OR v_charge_type <> 'pm.payable_charge' THEN
        RAISE EXCEPTION 'pm.payable_apply charge_document_id must reference pm.payable_charge (document_id=%, charge_document_id=%, type=%)', NEW.document_id, NEW.charge_document_id, v_charge_type
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_payable_apply__assert_document_refs ON doc_pm_payable_apply;
CREATE CONSTRAINT TRIGGER trg_doc_pm_payable_apply__assert_document_refs
    AFTER INSERT OR UPDATE OF credit_document_id, charge_document_id
    ON doc_pm_payable_apply
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_payable_apply_assert_document_refs();

-- ============================================================================
-- Maintenance Categories + Maintenance Requests (clean-slate baseline form)
-- ============================================================================

CREATE TABLE IF NOT EXISTS cat_pm_maintenance_category (
    catalog_id uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display    text NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_cat_pm_maintenance_category__display
    ON cat_pm_maintenance_category(display);

CREATE TABLE IF NOT EXISTS doc_pm_maintenance_request (
    document_id       uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display           text NOT NULL,
    property_id       uuid NOT NULL REFERENCES catalogs(id),
    party_id          uuid NOT NULL REFERENCES catalogs(id),
    category_id       uuid NOT NULL REFERENCES catalogs(id),
    priority          text NOT NULL,
    subject           text NOT NULL,
    description       text NULL,
    requested_at_utc  date NOT NULL,
    CONSTRAINT ck_doc_pm_maintenance_request__priority
        CHECK (priority IN ('Emergency', 'High', 'Normal', 'Low'))
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_maintenance_request__display
    ON doc_pm_maintenance_request(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_maintenance_request__property_id
    ON doc_pm_maintenance_request(property_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_maintenance_request__party_id
    ON doc_pm_maintenance_request(party_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_maintenance_request__category_id
    ON doc_pm_maintenance_request(category_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_maintenance_request__priority
    ON doc_pm_maintenance_request(priority);

CREATE INDEX IF NOT EXISTS ix_doc_pm_maintenance_request__requested_at_utc
    ON doc_pm_maintenance_request(requested_at_utc);

CREATE INDEX IF NOT EXISTS ix_doc_pm_maintenance_request__property_requested_at_document
    ON doc_pm_maintenance_request(property_id, requested_at_utc DESC, document_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_maintenance_request__category_priority_requested_at_document
    ON doc_pm_maintenance_request(category_id, priority, requested_at_utc DESC, document_id);

CREATE OR REPLACE FUNCTION ngb_pm_maintenance_request_normalize_priority()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.priority := CASE UPPER(BTRIM(COALESCE(NEW.priority, '')))
        WHEN 'EMERGENCY' THEN 'Emergency'
        WHEN 'HIGH' THEN 'High'
        WHEN 'NORMAL' THEN 'Normal'
        WHEN 'LOW' THEN 'Low'
        ELSE NEW.priority
    END;

    NEW.display := ngb_pm_build_document_display('Maintenance Request', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_maintenance_request__normalize_priority_and_display ON doc_pm_maintenance_request;
CREATE TRIGGER trg_doc_pm_maintenance_request__normalize_priority_and_display
BEFORE INSERT OR UPDATE ON doc_pm_maintenance_request
FOR EACH ROW
EXECUTE FUNCTION ngb_pm_maintenance_request_normalize_priority();

CREATE OR REPLACE FUNCTION ngb_pm_maintenance_request_assert_property_active()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_is_deleted boolean;
BEGIN
    SELECT c.is_deleted
      INTO v_is_deleted
      FROM cat_pm_property p
      JOIN catalogs c ON c.id = p.catalog_id
     WHERE p.catalog_id = NEW.property_id;

    IF v_is_deleted IS NULL THEN
        RAISE EXCEPTION 'pm.maintenance_request property_id must reference pm.property (document_id=%, property_id=%)', NEW.document_id, NEW.property_id
            USING ERRCODE = '23514';
    END IF;

    IF v_is_deleted THEN
        RAISE EXCEPTION 'pm.maintenance_request property is deleted (document_id=%, property_id=%)', NEW.document_id, NEW.property_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_maintenance_request__assert_property_active ON doc_pm_maintenance_request;
CREATE CONSTRAINT TRIGGER trg_doc_pm_maintenance_request__assert_property_active
    AFTER INSERT OR UPDATE OF property_id
    ON doc_pm_maintenance_request
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_maintenance_request_assert_property_active();

CREATE OR REPLACE FUNCTION ngb_pm_maintenance_request_assert_party_active()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_catalog_code text;
    v_is_deleted boolean;
BEGIN
    SELECT c.catalog_code, c.is_deleted
      INTO v_catalog_code, v_is_deleted
      FROM catalogs c
     WHERE c.id = NEW.party_id;

    IF v_catalog_code IS NULL OR v_catalog_code <> 'pm.party' THEN
        RAISE EXCEPTION 'pm.maintenance_request party_id must reference pm.party (document_id=%, party_id=%, catalog_code=%)', NEW.document_id, NEW.party_id, v_catalog_code
            USING ERRCODE = '23514';
    END IF;

    IF v_is_deleted THEN
        RAISE EXCEPTION 'pm.maintenance_request party is deleted (document_id=%, party_id=%)', NEW.document_id, NEW.party_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_maintenance_request__assert_party_active ON doc_pm_maintenance_request;
CREATE CONSTRAINT TRIGGER trg_doc_pm_maintenance_request__assert_party_active
    AFTER INSERT OR UPDATE OF party_id
    ON doc_pm_maintenance_request
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_maintenance_request_assert_party_active();

CREATE OR REPLACE FUNCTION ngb_pm_maintenance_request_assert_category_active()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_catalog_code text;
    v_is_deleted boolean;
BEGIN
    SELECT c.catalog_code, c.is_deleted
      INTO v_catalog_code, v_is_deleted
      FROM catalogs c
     WHERE c.id = NEW.category_id;

    IF v_catalog_code IS NULL OR v_catalog_code <> 'pm.maintenance_category' THEN
        RAISE EXCEPTION 'pm.maintenance_request category_id must reference pm.maintenance_category (document_id=%, category_id=%, catalog_code=%)', NEW.document_id, NEW.category_id, v_catalog_code
            USING ERRCODE = '23514';
    END IF;

    IF v_is_deleted THEN
        RAISE EXCEPTION 'pm.maintenance_request category is deleted (document_id=%, category_id=%)', NEW.document_id, NEW.category_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_maintenance_request__assert_category_active ON doc_pm_maintenance_request;
CREATE CONSTRAINT TRIGGER trg_doc_pm_maintenance_request__assert_category_active
    AFTER INSERT OR UPDATE OF category_id
    ON doc_pm_maintenance_request
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_maintenance_request_assert_category_active();

CREATE TABLE IF NOT EXISTS doc_pm_work_order (
    document_id           uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display               text NOT NULL,
    request_id            uuid NOT NULL REFERENCES documents(id),
    assigned_party_id     uuid NULL REFERENCES catalogs(id),
    scope_of_work         text NULL,
    due_by_utc            date NULL,
    cost_responsibility   text NOT NULL,
    CONSTRAINT ck_doc_pm_work_order__cost_responsibility
        CHECK (cost_responsibility IN ('Owner', 'Tenant', 'Company', 'Unknown'))
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_work_order__display
    ON doc_pm_work_order(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_work_order__request_id
    ON doc_pm_work_order(request_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_work_order__assigned_party_id
    ON doc_pm_work_order(assigned_party_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_work_order__due_by_utc
    ON doc_pm_work_order(due_by_utc);

CREATE INDEX IF NOT EXISTS ix_doc_pm_work_order__cost_responsibility
    ON doc_pm_work_order(cost_responsibility);

CREATE INDEX IF NOT EXISTS ix_doc_pm_work_order__request_assigned_due_document
    ON doc_pm_work_order(request_id, assigned_party_id, due_by_utc, document_id);

CREATE OR REPLACE FUNCTION ngb_pm_work_order_normalize_cost_responsibility()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.cost_responsibility := CASE UPPER(BTRIM(COALESCE(NEW.cost_responsibility, '')))
        WHEN 'OWNER' THEN 'Owner'
        WHEN 'TENANT' THEN 'Tenant'
        WHEN 'COMPANY' THEN 'Company'
        WHEN 'UNKNOWN' THEN 'Unknown'
        ELSE NEW.cost_responsibility
    END;

    NEW.display := ngb_pm_build_document_display('Work Order', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_work_order__normalize_cost_responsibility_and_display ON doc_pm_work_order;
CREATE TRIGGER trg_doc_pm_work_order__normalize_cost_responsibility_and_display
BEFORE INSERT OR UPDATE ON doc_pm_work_order
FOR EACH ROW
EXECUTE FUNCTION ngb_pm_work_order_normalize_cost_responsibility();

CREATE OR REPLACE FUNCTION ngb_pm_work_order_assert_request_ref()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_type_code text;
BEGIN
    SELECT d.type_code
      INTO v_type_code
      FROM documents d
     WHERE d.id = NEW.request_id;

    IF v_type_code IS NULL OR v_type_code <> 'pm.maintenance_request' THEN
        RAISE EXCEPTION 'pm.work_order request_id must reference pm.maintenance_request (document_id=%, request_id=%, type_code=%)', NEW.document_id, NEW.request_id, v_type_code
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_work_order__assert_request_ref ON doc_pm_work_order;
CREATE CONSTRAINT TRIGGER trg_doc_pm_work_order__assert_request_ref
    AFTER INSERT OR UPDATE OF request_id
    ON doc_pm_work_order
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_work_order_assert_request_ref();

CREATE OR REPLACE FUNCTION ngb_pm_work_order_assert_assigned_party_active()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_catalog_code text;
    v_is_deleted boolean;
BEGIN
    IF NEW.assigned_party_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT c.catalog_code, c.is_deleted
      INTO v_catalog_code, v_is_deleted
      FROM catalogs c
     WHERE c.id = NEW.assigned_party_id;

    IF v_catalog_code IS NULL OR v_catalog_code <> 'pm.party' THEN
        RAISE EXCEPTION 'pm.work_order assigned_party_id must reference pm.party (document_id=%, assigned_party_id=%, catalog_code=%)', NEW.document_id, NEW.assigned_party_id, v_catalog_code
            USING ERRCODE = '23514';
    END IF;

    IF v_is_deleted THEN
        RAISE EXCEPTION 'pm.work_order assigned party is deleted (document_id=%, assigned_party_id=%)', NEW.document_id, NEW.assigned_party_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_work_order__assert_assigned_party_active ON doc_pm_work_order;
CREATE CONSTRAINT TRIGGER trg_doc_pm_work_order__assert_assigned_party_active
    AFTER INSERT OR UPDATE OF assigned_party_id
    ON doc_pm_work_order
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_work_order_assert_assigned_party_active();

SELECT ngb_install_mirrored_document_relationship_trigger('doc_pm_work_order', 'request_id', 'created_from');

CREATE TABLE IF NOT EXISTS doc_pm_work_order_completion (
    document_id         uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    display             text NOT NULL,
    work_order_id       uuid NOT NULL REFERENCES documents(id),
    closed_at_utc       date NOT NULL,
    outcome             text NOT NULL,
    resolution_notes    text NULL,
    CONSTRAINT ck_doc_pm_work_order_completion__outcome
        CHECK (outcome IN ('Completed', 'Cancelled', 'UnableToComplete'))
);

CREATE INDEX IF NOT EXISTS ix_doc_pm_work_order_completion__display
    ON doc_pm_work_order_completion(display);

CREATE INDEX IF NOT EXISTS ix_doc_pm_work_order_completion__work_order_id
    ON doc_pm_work_order_completion(work_order_id);

CREATE INDEX IF NOT EXISTS ix_doc_pm_work_order_completion__closed_at_utc
    ON doc_pm_work_order_completion(closed_at_utc);

CREATE INDEX IF NOT EXISTS ix_doc_pm_work_order_completion__outcome
    ON doc_pm_work_order_completion(outcome);

CREATE INDEX IF NOT EXISTS ix_doc_pm_work_order_completion__work_order_closed_document
    ON doc_pm_work_order_completion(work_order_id, closed_at_utc, document_id);

CREATE OR REPLACE FUNCTION ngb_pm_work_order_completion_normalize_outcome()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.outcome := CASE UPPER(REPLACE(REPLACE(REPLACE(BTRIM(COALESCE(NEW.outcome, '')), '-', ''), '_', ''), ' ', ''))
        WHEN 'COMPLETED' THEN 'Completed'
        WHEN 'CANCELLED' THEN 'Cancelled'
        WHEN 'UNABLETOCOMPLETE' THEN 'UnableToComplete'
        ELSE NEW.outcome
    END;

    NEW.display := ngb_pm_build_document_display('Work Order Completion', NEW.document_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_work_order_completion__normalize_outcome_and_display ON doc_pm_work_order_completion;
CREATE TRIGGER trg_doc_pm_work_order_completion__normalize_outcome_and_display
BEFORE INSERT OR UPDATE ON doc_pm_work_order_completion
FOR EACH ROW
EXECUTE FUNCTION ngb_pm_work_order_completion_normalize_outcome();

CREATE OR REPLACE FUNCTION ngb_pm_work_order_completion_assert_work_order_ref()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_type_code text;
BEGIN
    SELECT d.type_code
      INTO v_type_code
      FROM documents d
     WHERE d.id = NEW.work_order_id;

    IF v_type_code IS NULL OR v_type_code <> 'pm.work_order' THEN
        RAISE EXCEPTION 'pm.work_order_completion work_order_id must reference pm.work_order (document_id=%, work_order_id=%, type_code=%)', NEW.document_id, NEW.work_order_id, v_type_code
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_doc_pm_work_order_completion__assert_work_order_ref ON doc_pm_work_order_completion;
CREATE CONSTRAINT TRIGGER trg_doc_pm_work_order_completion__assert_work_order_ref
    AFTER INSERT OR UPDATE OF work_order_id
    ON doc_pm_work_order_completion
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
EXECUTE FUNCTION ngb_pm_work_order_completion_assert_work_order_ref();

SELECT ngb_install_mirrored_document_relationship_trigger('doc_pm_work_order_completion', 'work_order_id', 'created_from');

CREATE OR REPLACE FUNCTION ngb_pm_work_order_completion_assert_single_posted()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_work_order_id uuid;
    v_work_order_status smallint;
    v_existing_document_id uuid;
BEGIN
    SELECT c.work_order_id
      INTO v_work_order_id
      FROM doc_pm_work_order_completion c
     WHERE c.document_id = NEW.id;

    IF v_work_order_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT d.status
      INTO v_work_order_status
      FROM documents d
     WHERE d.id = v_work_order_id;

    IF v_work_order_status IS NULL OR v_work_order_status <> 2 THEN
        RAISE EXCEPTION 'pm.work_order_completion referenced work order must be posted (document_id=%, work_order_id=%, status=%)', NEW.id, v_work_order_id, v_work_order_status
            USING ERRCODE = '23514';
    END IF;

    SELECT c.document_id
      INTO v_existing_document_id
      FROM doc_pm_work_order_completion c
      JOIN documents d
        ON d.id = c.document_id
     WHERE c.work_order_id = v_work_order_id
       AND d.status = 2
       AND c.document_id <> NEW.id
     LIMIT 1;

    IF v_existing_document_id IS NOT NULL THEN
        RAISE EXCEPTION 'pm.work_order_completion only one posted completion is allowed per work order (document_id=%, work_order_id=%, existing_document_id=%)', NEW.id, v_work_order_id, v_existing_document_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_documents__pm_work_order_completion_assert_single_posted ON documents;
CREATE CONSTRAINT TRIGGER trg_documents__pm_work_order_completion_assert_single_posted
    AFTER UPDATE OF status
    ON documents
    DEFERRABLE INITIALLY IMMEDIATE
    FOR EACH ROW
    WHEN (NEW.type_code = 'pm.work_order_completion' AND NEW.status = 2)
EXECUTE FUNCTION ngb_pm_work_order_completion_assert_single_posted();

CREATE OR REPLACE FUNCTION ngb_pm_refresh_document_display_from_header()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    CASE NEW.type_code
        WHEN 'pm.maintenance_request' THEN
            UPDATE doc_pm_maintenance_request
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.work_order' THEN
            UPDATE doc_pm_work_order
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.work_order_completion' THEN
            UPDATE doc_pm_work_order_completion
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.rent_charge' THEN
            UPDATE doc_pm_rent_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_charge' THEN
            UPDATE doc_pm_receivable_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.late_fee_charge' THEN
            UPDATE doc_pm_late_fee_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_payment' THEN
            UPDATE doc_pm_receivable_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_returned_payment' THEN
            UPDATE doc_pm_receivable_returned_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_credit_memo' THEN
            UPDATE doc_pm_receivable_credit_memo
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.payable_charge' THEN
            UPDATE doc_pm_payable_charge
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.payable_payment' THEN
            UPDATE doc_pm_payable_payment
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.payable_credit_memo' THEN
            UPDATE doc_pm_payable_credit_memo
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.receivable_apply' THEN
            UPDATE doc_pm_receivable_apply
               SET display = display
             WHERE document_id = NEW.id;
        WHEN 'pm.payable_apply' THEN
            UPDATE doc_pm_payable_apply
               SET display = display
             WHERE document_id = NEW.id;
    END CASE;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_documents__pm_refresh_typed_display ON documents;
CREATE TRIGGER trg_documents__pm_refresh_typed_display
AFTER UPDATE OF number, date_utc ON documents
FOR EACH ROW
WHEN (NEW.type_code IN ('pm.maintenance_request', 'pm.work_order', 'pm.work_order_completion', 'pm.rent_charge', 'pm.receivable_charge', 'pm.late_fee_charge', 'pm.receivable_payment', 'pm.receivable_returned_payment', 'pm.receivable_credit_memo', 'pm.payable_charge', 'pm.payable_payment', 'pm.payable_credit_memo', 'pm.receivable_apply', 'pm.payable_apply'))
EXECUTE FUNCTION ngb_pm_refresh_document_display_from_header();

-- Every PM typed document table must be guarded by the shared posted-document immutability trigger.
-- The platform pack provides the installer function; PM pack depends on platform.
SELECT ngb_install_typed_document_immutability_guards();
