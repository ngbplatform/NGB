-- NGB.Platform clean baseline for recreated databases.
--
-- Scope:
-- - final platform schema state for the platform pack
-- - supersedes the historical platform versioned/repeatable migrations
-- - no business seed data
SET TIME ZONE 'UTC';

-- >>> PlatformAppendOnlyGuardFunctionMigration
                                CREATE OR REPLACE FUNCTION ngb_forbid_mutation_of_append_only_table()
                                RETURNS trigger AS $$
                                BEGIN
                                    RAISE EXCEPTION 'Append-only table cannot be mutated: %', TG_TABLE_NAME
                                        USING ERRCODE = '55000';
                                END;
                                $$ LANGUAGE plpgsql;
-- <<< PlatformAppendOnlyGuardFunctionMigration

-- >>> PlatformDimensionsMigration
                                CREATE TABLE IF NOT EXISTS platform_dimensions (
                                    dimension_id UUID PRIMARY KEY,

                                    code TEXT NOT NULL,
                                    name TEXT NOT NULL,

                                    is_active BOOLEAN NOT NULL DEFAULT TRUE,
                                    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT ck_platform_dimensions_code_nonempty CHECK (length(trim(code)) > 0),
                                    CONSTRAINT ck_platform_dimensions_name_nonempty CHECK (length(trim(name)) > 0)
                                );
-- <<< PlatformDimensionsMigration

-- >>> PlatformDimensionsCodeNormMigration
                                ALTER TABLE platform_dimensions
                                    ADD COLUMN IF NOT EXISTS code_norm TEXT GENERATED ALWAYS AS (lower(btrim(code))) STORED;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_platform_dimensions_code_trimmed'
                                    ) THEN
                                        ALTER TABLE platform_dimensions
                                            ADD CONSTRAINT ck_platform_dimensions_code_trimmed
                                            CHECK (code = btrim(code));
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_platform_dimensions_name_trimmed'
                                    ) THEN
                                        ALTER TABLE platform_dimensions
                                            ADD CONSTRAINT ck_platform_dimensions_name_trimmed
                                            CHECK (name = btrim(name));
                                    END IF;
                                END
                                $$;
-- <<< PlatformDimensionsCodeNormMigration

-- >>> PlatformDimensionSetsMigration
                                CREATE TABLE IF NOT EXISTS platform_dimension_sets (
                                    dimension_set_id UUID PRIMARY KEY,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
                                );

                                -- Reserved empty set (no dimensions)
                                INSERT INTO platform_dimension_sets (dimension_set_id)
                                VALUES ('00000000-0000-0000-0000-000000000000')
                                ON CONFLICT (dimension_set_id) DO NOTHING;
-- <<< PlatformDimensionSetsMigration

-- >>> PlatformDimensionSetItemsMigration
                                CREATE TABLE IF NOT EXISTS platform_dimension_set_items
                                (
                                    dimension_set_id UUID NOT NULL,
                                    dimension_id     UUID NOT NULL,
                                    value_id         UUID NOT NULL,

                                    created_at_utc   TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT pk_platform_dimset_items PRIMARY KEY (dimension_set_id, dimension_id),

                                    CONSTRAINT ck_platform_dimset_items_set_nonempty
                                        CHECK (dimension_set_id <> '00000000-0000-0000-0000-000000000000'::uuid),
                                    CONSTRAINT ck_platform_dimset_items_value_nonempty
                                        CHECK (value_id <> '00000000-0000-0000-0000-000000000000'::uuid),

                                    CONSTRAINT fk_platform_dimset_items_set
                                        FOREIGN KEY (dimension_set_id)
                                        REFERENCES platform_dimension_sets(dimension_set_id)
                                        ON DELETE RESTRICT,
                                    CONSTRAINT fk_platform_dimset_items_dimension
                                        FOREIGN KEY (dimension_id)
                                        REFERENCES platform_dimensions(dimension_id)
                                        ON DELETE RESTRICT
                                );

                                -- Drift repair: if a test (or manual drift) drops FK constraints, CREATE TABLE IF NOT EXISTS won't restore them.
                                -- We keep this block cheap in the steady state by first checking for canonical constraint names.
                                DO $$
                                DECLARE
                                    has_set boolean;
                                    has_dimension boolean;
                                    r record;
                                BEGIN
                                    SELECT EXISTS(
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'platform_dimension_set_items'::regclass
                                          AND conname = 'fk_platform_dimset_items_set'
                                    ) INTO has_set;

                                    SELECT EXISTS(
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'platform_dimension_set_items'::regclass
                                          AND conname = 'fk_platform_dimset_items_dimension'
                                    ) INTO has_dimension;

                                    IF has_set AND has_dimension THEN
                                        RETURN;
                                    END IF;

                                    FOR r IN
                                        SELECT conname
                                        FROM pg_constraint
                                        WHERE conrelid = 'platform_dimension_set_items'::regclass
                                          AND contype = 'f'
                                    LOOP
                                        EXECUTE format('ALTER TABLE platform_dimension_set_items DROP CONSTRAINT IF EXISTS %I', r.conname);
                                    END LOOP;

                                    ALTER TABLE platform_dimension_set_items
                                        ADD CONSTRAINT fk_platform_dimset_items_set
                                            FOREIGN KEY (dimension_set_id)
                                            REFERENCES platform_dimension_sets(dimension_set_id)
                                            ON DELETE RESTRICT;

                                    ALTER TABLE platform_dimension_set_items
                                        ADD CONSTRAINT fk_platform_dimset_items_dimension
                                            FOREIGN KEY (dimension_id)
                                            REFERENCES platform_dimensions(dimension_id)
                                            ON DELETE RESTRICT;
                                END $$;
-- <<< PlatformDimensionSetItemsMigration

-- >>> PlatformDimensionSetItemsConstraintsDriftRepairMigration
                                DO $$
                                BEGIN
                                    IF to_regclass('public.platform_dimension_set_items') IS NULL THEN
                                        RETURN;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_platform_dimset_items_set_nonempty'
                                    ) THEN
                                        ALTER TABLE public.platform_dimension_set_items
                                            ADD CONSTRAINT ck_platform_dimset_items_set_nonempty
                                                CHECK (dimension_set_id <> '00000000-0000-0000-0000-000000000000');
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_platform_dimset_items_dimension_nonempty'
                                    ) THEN
                                        ALTER TABLE public.platform_dimension_set_items
                                            ADD CONSTRAINT ck_platform_dimset_items_dimension_nonempty
                                                CHECK (dimension_id <> '00000000-0000-0000-0000-000000000000');
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_platform_dimset_items_value_nonempty'
                                    ) THEN
                                        ALTER TABLE public.platform_dimension_set_items
                                            ADD CONSTRAINT ck_platform_dimset_items_value_nonempty
                                                CHECK (value_id <> '00000000-0000-0000-0000-000000000000');
                                    END IF;
                                END
                                $$;
-- <<< PlatformDimensionSetItemsConstraintsDriftRepairMigration

-- >>> PlatformDimensionsIndexesMigration
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_platform_dimensions_code_norm_not_deleted
                                    ON platform_dimensions(code_norm);

                                CREATE INDEX IF NOT EXISTS ix_platform_dimensions_is_active
                                    ON platform_dimensions(is_active)
                                    WHERE is_deleted = FALSE;
-- <<< PlatformDimensionsIndexesMigration

-- >>> PlatformDimensionSetItemsIndexesMigration
                                CREATE INDEX IF NOT EXISTS ix_platform_dimset_items_set
                                    ON platform_dimension_set_items(dimension_set_id);

                                CREATE INDEX IF NOT EXISTS ix_platform_dimset_items_dimension_value_set
                                    ON platform_dimension_set_items(dimension_id, value_id, dimension_set_id);
-- <<< PlatformDimensionSetItemsIndexesMigration

-- >>> PlatformDimensionSetsAppendOnlyGuardMigration
                                DO $$
                                BEGIN
                                    IF to_regclass('public.platform_dimension_sets') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_platform_dimension_sets_append_only ON public.platform_dimension_sets;
                                        CREATE TRIGGER trg_platform_dimension_sets_append_only
                                            BEFORE UPDATE OR DELETE ON public.platform_dimension_sets
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;

                                    IF to_regclass('public.platform_dimension_set_items') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_platform_dimension_set_items_append_only ON public.platform_dimension_set_items;
                                        CREATE TRIGGER trg_platform_dimension_set_items_append_only
                                            BEFORE UPDATE OR DELETE ON public.platform_dimension_set_items
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;
                                END
                                $$;
-- <<< PlatformDimensionSetsAppendOnlyGuardMigration

-- >>> AccountingAccountsMigration
                                CREATE TABLE IF NOT EXISTS accounting_accounts (
                                    account_id UUID PRIMARY KEY,

                                    code TEXT NOT NULL,
                                    name TEXT NOT NULL,
                                    account_type SMALLINT NOT NULL,
                                    statement_section SMALLINT NOT NULL,

                                    is_contra BOOLEAN NOT NULL DEFAULT FALSE,

                                    negative_balance_policy SMALLINT NOT NULL,

                                    is_active BOOLEAN NOT NULL DEFAULT TRUE,
                                    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT ck_acc_accounts_code_nonempty CHECK (length(trim(code)) > 0),
                                    CONSTRAINT ck_acc_accounts_name_nonempty CHECK (length(trim(name)) > 0),
                                    CONSTRAINT ck_acc_accounts_statement_section_range CHECK (statement_section BETWEEN 1 AND 8)
                                );
-- <<< AccountingAccountsMigration

-- >>> AccountingAccountsCodeNormMigration
                                -- Add normalized code column (idempotent)
                                ALTER TABLE accounting_accounts
                                    ADD COLUMN IF NOT EXISTS code_norm TEXT GENERATED ALWAYS AS (lower(btrim(code))) STORED;

                                -- Optional hardening: enforce trimming (idempotent via DO blocks)
                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_acc_accounts_code_trimmed'
                                    ) THEN
                                        ALTER TABLE accounting_accounts
                                            ADD CONSTRAINT ck_acc_accounts_code_trimmed
                                            CHECK (code = btrim(code));
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_acc_accounts_name_trimmed'
                                    ) THEN
                                        ALTER TABLE accounting_accounts
                                            ADD CONSTRAINT ck_acc_accounts_name_trimmed
                                            CHECK (name = btrim(name));
                                    END IF;
                                END
                                $$;
-- <<< AccountingAccountsCodeNormMigration

-- >>> AccountingBalancesMigration
                                CREATE TABLE IF NOT EXISTS accounting_balances (
                                    period DATE NOT NULL,
                                    account_id UUID NOT NULL,
                                    dimension_set_id UUID NOT NULL,

                                    opening_balance NUMERIC NOT NULL DEFAULT 0,
                                    closing_balance NUMERIC NOT NULL DEFAULT 0,

                                    PRIMARY KEY (period, account_id, dimension_set_id),

                                    CONSTRAINT chk_acc_balances_period_month_start
                                        CHECK (period = DATE_TRUNC('month', period)::date),

                                    CONSTRAINT fk_acc_balances_account
                                        FOREIGN KEY (account_id) REFERENCES accounting_accounts(account_id),

                                    CONSTRAINT fk_acc_balances_dimension_set
                                        FOREIGN KEY (dimension_set_id) REFERENCES platform_dimension_sets(dimension_set_id)
                                );
-- <<< AccountingBalancesMigration

-- >>> AccountingClosedPeriodsMigration
                                CREATE TABLE IF NOT EXISTS accounting_closed_periods (
                                    period DATE PRIMARY KEY,
                                    closed_at_utc TIMESTAMPTZ NOT NULL,
                                    closed_by TEXT NOT NULL,

                                    CONSTRAINT ck_closed_periods_month CHECK (EXTRACT(DAY FROM period) = 1)
                                );
-- <<< AccountingClosedPeriodsMigration

-- >>> AccountingPostingStateMigration
                                DO $$
                                BEGIN
                                    IF to_regclass('public.accounting_posting_state') IS NULL
                                       AND to_regclass('public.accounting_posting_log') IS NOT NULL THEN
                                        ALTER TABLE accounting_posting_log RENAME TO accounting_posting_state;
                                    END IF;
                                END
                                $$;

                                CREATE TABLE IF NOT EXISTS accounting_posting_state (
                                    document_id      uuid         NOT NULL,
                                    operation        smallint     NOT NULL,

                                    attempt_id       uuid         NULL,
                                    started_at_utc   timestamptz  NOT NULL,
                                    completed_at_utc timestamptz  NULL,

                                    CONSTRAINT pk_accounting_posting_state PRIMARY KEY (document_id, operation),

                                    -- Allowed operations (see NGB.Persistence.PostingState.PostingOperation):
                                    -- 1 = Post, 2 = Unpost, 3 = Repost, 4 = CloseFiscalYear
                                    CONSTRAINT ck_accounting_posting_state_operation
                                        CHECK (operation IN (1, 2, 3, 4)),

                                    CONSTRAINT ck_accounting_posting_state_completed_after_started
                                        CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)
                                );

                                ALTER TABLE accounting_posting_state
                                    ADD COLUMN IF NOT EXISTS attempt_id uuid;
-- <<< AccountingPostingStateMigration

-- >>> AccountingPostingLogHistoryMigration
                                CREATE TABLE IF NOT EXISTS accounting_posting_log_history (
                                    history_id       uuid         PRIMARY KEY,
                                    attempt_id       uuid         NOT NULL,
                                    document_id      uuid         NOT NULL,
                                    operation        smallint     NOT NULL,
                                    event_kind       smallint     NOT NULL,
                                    occurred_at_utc  timestamptz  NOT NULL,

                                    CONSTRAINT ck_accounting_posting_log_history_operation
                                        CHECK (operation IN (1, 2, 3, 4)),

                                    CONSTRAINT ck_accounting_posting_log_history_event_kind
                                        CHECK (event_kind IN (1, 2, 3))
                                );

                                CREATE INDEX IF NOT EXISTS ix_accounting_posting_log_history_document_operation_occurred
                                    ON accounting_posting_log_history(document_id, operation, occurred_at_utc DESC);

                                CREATE INDEX IF NOT EXISTS ix_accounting_posting_log_history_attempt
                                    ON accounting_posting_log_history(attempt_id, occurred_at_utc);

                                DO $$
                                BEGIN
                                    IF to_regclass('public.accounting_posting_log_history') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_accounting_posting_log_history_append_only ON public.accounting_posting_log_history;
                                        CREATE TRIGGER trg_accounting_posting_log_history_append_only
                                            BEFORE UPDATE OR DELETE ON public.accounting_posting_log_history
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;
                                END
                                $$;
-- <<< AccountingPostingLogHistoryMigration

-- >>> AccountingRegisterMigration
                                CREATE TABLE IF NOT EXISTS accounting_register_main (
                                    entry_id BIGSERIAL PRIMARY KEY,

                                    document_id UUID NOT NULL,
                                    period TIMESTAMPTZ NOT NULL,

                                    period_month DATE GENERATED ALWAYS AS (date_trunc('month', (period AT TIME ZONE 'UTC'))::date) STORED,

                                    debit_account_id  UUID NOT NULL,
                                    credit_account_id UUID NOT NULL,

                                    -- DimensionSetId is the long-term key for analytical dimensions.
                                    -- For empty dimensions it is Guid.Empty (no dimension set row is created).
                                    debit_dimension_set_id  UUID NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
                                    credit_dimension_set_id UUID NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',

                                    amount NUMERIC(18,4) NOT NULL,
                                    is_storno BOOLEAN NOT NULL DEFAULT FALSE,

                                    -- DB-level invariants
                                    CONSTRAINT ck_acc_reg_amount_positive CHECK (amount > 0),
                                    CONSTRAINT ck_acc_reg_debit_not_equal_credit CHECK (debit_account_id <> credit_account_id),

                                    CONSTRAINT fk_acc_reg_debit_account
                                        FOREIGN KEY (debit_account_id) REFERENCES accounting_accounts(account_id),

                                    CONSTRAINT fk_acc_reg_credit_account
                                        FOREIGN KEY (credit_account_id) REFERENCES accounting_accounts(account_id)
                                );
-- <<< AccountingRegisterMigration

-- >>> AccountingRegisterDimensionSetForeignKeysMigration
                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'fk_acc_reg_debit_dimension_set'
                                    ) THEN
                                        ALTER TABLE accounting_register_main
                                            ADD CONSTRAINT fk_acc_reg_debit_dimension_set
                                                FOREIGN KEY (debit_dimension_set_id)
                                                REFERENCES platform_dimension_sets(dimension_set_id)
                                                ON DELETE RESTRICT;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'fk_acc_reg_credit_dimension_set'
                                    ) THEN
                                        ALTER TABLE accounting_register_main
                                            ADD CONSTRAINT fk_acc_reg_credit_dimension_set
                                                FOREIGN KEY (credit_dimension_set_id)
                                                REFERENCES platform_dimension_sets(dimension_set_id)
                                                ON DELETE RESTRICT;
                                    END IF;
                                END
                                $$;
-- <<< AccountingRegisterDimensionSetForeignKeysMigration

-- >>> AccountingTurnoversMigration
                                CREATE TABLE IF NOT EXISTS accounting_turnovers (
                                    period DATE NOT NULL,
                                    account_id UUID NOT NULL,
                                    dimension_set_id UUID NOT NULL,

                                    debit_amount  NUMERIC NOT NULL DEFAULT 0,
                                    credit_amount NUMERIC NOT NULL DEFAULT 0,

                                    PRIMARY KEY (period, account_id, dimension_set_id),

                                    CONSTRAINT chk_acc_turnovers_period_month_start
                                        CHECK (period = DATE_TRUNC('month', period)::date),

                                    CONSTRAINT fk_acc_turnovers_account
                                        FOREIGN KEY (account_id) REFERENCES accounting_accounts(account_id),

                                    CONSTRAINT fk_acc_turnovers_dimension_set
                                        FOREIGN KEY (dimension_set_id) REFERENCES platform_dimension_sets(dimension_set_id)
                                );
-- <<< AccountingTurnoversMigration

-- >>> AccountingClosedPeriodsGuardMigration
                                CREATE OR REPLACE FUNCTION ngb_forbid_posting_into_closed_period()
                                RETURNS trigger AS $$
                                DECLARE
                                    p DATE;
                                    is_date BOOLEAN;
                                BEGIN
                                    -- period_month is generated on register_main, but we compute it defensively.
                                    -- IMPORTANT: handle DELETE too (NEW is NULL for DELETE).
                                    IF TG_OP = 'DELETE' THEN
                                        is_date := pg_typeof(OLD.period) = 'date'::regtype;
                                        IF is_date THEN
                                            p := date_trunc('month', OLD.period::timestamp)::date;
                                        ELSE
                                            p := date_trunc('month', (OLD.period AT TIME ZONE 'UTC'))::date;
                                        END IF;
                                    ELSE
                                        is_date := pg_typeof(NEW.period) = 'date'::regtype;
                                        IF is_date THEN
                                            p := date_trunc('month', NEW.period::timestamp)::date;
                                        ELSE
                                            p := date_trunc('month', (NEW.period AT TIME ZONE 'UTC'))::date;
                                        END IF;
                                    END IF;

                                    IF EXISTS (
                                        SELECT 1
                                        FROM accounting_closed_periods cp
                                        WHERE cp.period = p
                                    ) THEN
                                        RAISE EXCEPTION 'Posting is forbidden. Period is closed: %', p;
                                    END IF;

                                    IF TG_OP = 'DELETE' THEN
                                        RETURN OLD;
                                    END IF;

                                    RETURN NEW;
                                END;
                                $$ LANGUAGE plpgsql;

                                -- accounting_register_main
                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_trigger
                                        WHERE tgname = 'trg_acc_reg_no_closed_period'
                                    ) THEN
                                        CREATE TRIGGER trg_acc_reg_no_closed_period
                                            BEFORE INSERT OR UPDATE ON accounting_register_main
                                            FOR EACH ROW
                                            EXECUTE FUNCTION ngb_forbid_posting_into_closed_period();
                                    END IF;
                                END $$;

                                -- Existing installations may already have INSERT/UPDATE trigger.
                                -- Add a dedicated DELETE trigger for defense in depth.
                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_trigger
                                        WHERE tgname = 'trg_acc_reg_no_closed_period_delete'
                                    ) THEN
                                        CREATE TRIGGER trg_acc_reg_no_closed_period_delete
                                            BEFORE DELETE ON accounting_register_main
                                            FOR EACH ROW
                                            EXECUTE FUNCTION ngb_forbid_posting_into_closed_period();
                                    END IF;
                                END $$;

                                -- accounting_turnovers (defense in depth)
                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_trigger
                                        WHERE tgname = 'trg_acc_turnovers_no_closed_period'
                                    ) THEN
                                        CREATE TRIGGER trg_acc_turnovers_no_closed_period
                                            BEFORE INSERT OR UPDATE ON accounting_turnovers
                                            FOR EACH ROW
                                            EXECUTE FUNCTION ngb_forbid_posting_into_closed_period();
                                    END IF;
                                END $$;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_trigger
                                        WHERE tgname = 'trg_acc_turnovers_no_closed_period_delete'
                                    ) THEN
                                        CREATE TRIGGER trg_acc_turnovers_no_closed_period_delete
                                            BEFORE DELETE ON accounting_turnovers
                                            FOR EACH ROW
                                            EXECUTE FUNCTION ngb_forbid_posting_into_closed_period();
                                    END IF;
                                END $$;

                                -- accounting_balances (defense in depth)
                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_trigger
                                        WHERE tgname = 'trg_acc_balances_no_closed_period'
                                    ) THEN
                                        CREATE TRIGGER trg_acc_balances_no_closed_period
                                            BEFORE INSERT OR UPDATE ON accounting_balances
                                            FOR EACH ROW
                                            EXECUTE FUNCTION ngb_forbid_posting_into_closed_period();
                                    END IF;
                                END $$;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_trigger
                                        WHERE tgname = 'trg_acc_balances_no_closed_period_delete'
                                    ) THEN
                                        CREATE TRIGGER trg_acc_balances_no_closed_period_delete
                                            BEFORE DELETE ON accounting_balances
                                            FOR EACH ROW
                                            EXECUTE FUNCTION ngb_forbid_posting_into_closed_period();
                                    END IF;
                                END $$;
-- <<< AccountingClosedPeriodsGuardMigration

-- >>> AccountingAccountsIndexesMigration
                                -- Unique normalized code (case-insensitive, trim+lower) across ALL accounts (including deleted)
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_acc_accounts_code_norm
                                    ON accounting_accounts(code_norm);

                                CREATE INDEX IF NOT EXISTS ix_acc_accounts_is_active
                                    ON accounting_accounts(is_active)
                                    WHERE is_deleted = FALSE;
-- <<< AccountingAccountsIndexesMigration

-- >>> AccountingRegisterIndexesMigration
                                -- document lookups (reposting / storno)
                                CREATE INDEX IF NOT EXISTS ix_acc_reg_document_id
                                    ON accounting_register_main(document_id);


                                -- Faster Unpost/Repost lookups: select original (non-storno) rows for a document
                                -- and derive affected months without scanning storno rows.
                                CREATE INDEX IF NOT EXISTS ix_acc_reg_document_month_nostorno
                                    ON accounting_register_main (document_id, period_month)
                                    WHERE is_storno = FALSE;

                                -- month slicing (reports/closing)
                                CREATE INDEX IF NOT EXISTS ix_acc_reg_period_month
                                    ON accounting_register_main(period_month);

                                -- account + month (account card / turnovers by account)
                                CREATE INDEX IF NOT EXISTS ix_acc_reg_debit_account_month
                                    ON accounting_register_main(debit_account_id, period_month);

                                CREATE INDEX IF NOT EXISTS ix_acc_reg_credit_account_month
                                    ON accounting_register_main(credit_account_id, period_month);

                                -- account + month + dimension set (dimension-aware reports)
                                CREATE INDEX IF NOT EXISTS ix_acc_reg_debit_month_dimset
                                    ON accounting_register_main(period_month, debit_account_id, debit_dimension_set_id);

                                CREATE INDEX IF NOT EXISTS ix_acc_reg_credit_month_dimset
                                    ON accounting_register_main(period_month, credit_account_id, credit_dimension_set_id);
                                -- Optional (only if you often filter by both account and dimension set)

                                -- Optional for huge tables (append-only):
                                -- BRIN is extremely small and fast for time-range scans if data is inserted in time order.
                                CREATE INDEX IF NOT EXISTS brin_acc_reg_period
                                    ON accounting_register_main USING BRIN(period);
-- <<< AccountingRegisterIndexesMigration

-- >>> AccountingTurnoversIndexesMigration
                                -- pkey (period, account_id, dimension_set_id) covers most period scans.
                                -- Extra index helps account-centric reports (account card, drills):
                                CREATE INDEX IF NOT EXISTS ix_acc_turnovers_account_period
                                    ON accounting_turnovers (account_id, dimension_set_id, period);

                                -- Keep explicit period-first index name for explain-plan tests/diagnostics.
                                -- Note: this is redundant with the pkey, but is harmless and documents intent.
                                CREATE INDEX IF NOT EXISTS ix_acc_turnovers_period_account
                                    ON accounting_turnovers (period, account_id, dimension_set_id);
-- <<< AccountingTurnoversIndexesMigration

-- >>> AccountingBalancesIndexesMigration
                                -- pkey (period, account_id, dimension_set_id) covers most period scans.
                                -- Extra index helps account-centric reads.
                                CREATE INDEX IF NOT EXISTS ix_acc_balances_account_period
                                    ON accounting_balances (account_id, dimension_set_id, period);

                                CREATE INDEX IF NOT EXISTS ix_acc_balances_period_account
                                    ON accounting_balances (period, account_id, dimension_set_id);
-- <<< AccountingBalancesIndexesMigration

-- >>> AccountingPostingStateIndexesMigration
                                CREATE INDEX IF NOT EXISTS ix_accounting_posting_state_operation
                                    ON accounting_posting_state(operation);

                                CREATE INDEX IF NOT EXISTS ix_accounting_posting_state_started
                                    ON accounting_posting_state(started_at_utc);

                                CREATE INDEX IF NOT EXISTS ix_accounting_posting_state_completed
                                    ON accounting_posting_state(completed_at_utc);
-- <<< AccountingPostingStateIndexesMigration

-- >>> CatalogsMigration
                                CREATE TABLE IF NOT EXISTS catalogs (
                                    id              uuid            NOT NULL PRIMARY KEY,
                                    catalog_code     text            NOT NULL,
                                    is_deleted       boolean         NOT NULL DEFAULT FALSE,
                                    created_at_utc   timestamptz     NOT NULL DEFAULT NOW(),
                                    updated_at_utc   timestamptz     NOT NULL DEFAULT NOW()
                                );

                                -- Timestamp default drift repair:
                                -- for timestamptz "instants" we prefer DEFAULT NOW() (not "NOW() at time zone 'UTC'").
                                DO $$
                                BEGIN
                                  IF EXISTS (
                                    SELECT 1
                                      FROM information_schema.columns
                                     WHERE table_schema = 'public'
                                       AND table_name   = 'catalogs'
                                       AND column_name  = 'created_at_utc'
                                  ) THEN
                                    EXECUTE 'ALTER TABLE catalogs ALTER COLUMN created_at_utc SET DEFAULT NOW()';
                                  END IF;

                                  IF EXISTS (
                                    SELECT 1
                                      FROM information_schema.columns
                                     WHERE table_schema = 'public'
                                       AND table_name   = 'catalogs'
                                       AND column_name  = 'updated_at_utc'
                                  ) THEN
                                    EXECUTE 'ALTER TABLE catalogs ALTER COLUMN updated_at_utc SET DEFAULT NOW()';
                                  END IF;
                                END $$;

                                -- NOTE:
                                -- Catalogs follow the same hybrid approach as documents:
                                -- - Common header lives in 'catalogs'
                                -- - Per-type data belongs in: cat_{catalog_code}, cat_{catalog_code}__{part}, ...
-- <<< CatalogsMigration

-- >>> CatalogsIndexesMigration
                                CREATE INDEX IF NOT EXISTS ix_catalogs_catalog_code
                                    ON catalogs (catalog_code);

                                CREATE INDEX IF NOT EXISTS ix_catalogs_catalog_code_not_deleted
                                    ON catalogs (catalog_code)
                                    WHERE is_deleted = FALSE;
-- <<< CatalogsIndexesMigration

-- >>> DocumentsMigration
                                CREATE TABLE IF NOT EXISTS documents (
                                    id                          uuid         PRIMARY KEY,
                                    type_code                   text         NOT NULL,
                                    number                      text         NULL,

                                    date_utc                    timestamptz  NOT NULL,

                                    status                      smallint     NOT NULL,
                                    posted_at_utc               timestamptz  NULL,
                                    marked_for_deletion_at_utc  timestamptz  NULL,

                                    created_at_utc              timestamptz  NOT NULL DEFAULT NOW(),
                                    updated_at_utc              timestamptz  NOT NULL DEFAULT NOW(),

                                    -- Status values match NGB.Core.Documents.DocumentStatus:
                                    -- 1 = Draft, 2 = Posted, 3 = MarkedForDeletion
                                    CONSTRAINT ck_documents_status
                                        CHECK (status IN (1, 2, 3)),

                                    CONSTRAINT ck_documents_posted_state
                                        CHECK (
                                            (status = 2 AND posted_at_utc IS NOT NULL AND marked_for_deletion_at_utc IS NULL)
                                            OR
                                            (status <> 2 AND posted_at_utc IS NULL)
                                        ),

                                    CONSTRAINT ck_documents_marked_for_deletion_state
                                        CHECK (
                                            (status = 3 AND marked_for_deletion_at_utc IS NOT NULL AND posted_at_utc IS NULL)
                                            OR
                                            (status <> 3 AND marked_for_deletion_at_utc IS NULL)
                                        )
                                );

                                -- Timestamp default drift repair:
                                -- for timestamptz "instants" we prefer DEFAULT NOW() (not "NOW() at time zone 'UTC'").
                                DO $$
                                BEGIN
                                  IF EXISTS (
                                    SELECT 1
                                      FROM information_schema.columns
                                     WHERE table_schema = 'public'
                                       AND table_name   = 'documents'
                                       AND column_name  = 'created_at_utc'
                                  ) THEN
                                    EXECUTE 'ALTER TABLE documents ALTER COLUMN created_at_utc SET DEFAULT NOW()';
                                  END IF;

                                  IF EXISTS (
                                    SELECT 1
                                      FROM information_schema.columns
                                     WHERE table_schema = 'public'
                                       AND table_name   = 'documents'
                                       AND column_name  = 'updated_at_utc'
                                  ) THEN
                                    EXECUTE 'ALTER TABLE documents ALTER COLUMN updated_at_utc SET DEFAULT NOW()';
                                  END IF;
                                END $$;
-- <<< DocumentsMigration

-- >>> DocumentNumberSequencesMigration
                         CREATE TABLE IF NOT EXISTS document_number_sequences
                         (
                             type_code   text   NOT NULL,
                             fiscal_year integer NOT NULL,
                             last_seq    bigint NOT NULL,

                             CONSTRAINT pk_document_number_sequences
                                 PRIMARY KEY (type_code, fiscal_year),

                             CONSTRAINT ck_document_number_sequences_fiscal_year
                                 CHECK (fiscal_year >= 1900 AND fiscal_year <= 3000),

                             CONSTRAINT ck_document_number_sequences_last_seq
                                 CHECK (last_seq >= 1)
                         );
-- <<< DocumentNumberSequencesMigration

-- >>> DocumentsIndexesMigration
                                CREATE INDEX IF NOT EXISTS ix_documents_type_date
                                    ON documents(type_code, date_utc);

                                CREATE INDEX IF NOT EXISTS ix_documents_status
                                    ON documents(status);

                                CREATE INDEX IF NOT EXISTS ix_documents_number
                                    ON documents(number);

                                -- Enforce uniqueness of numbering per document type when the number is set.
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_documents_type_number_not_null
                                    ON documents(type_code, number)
                                    WHERE number IS NOT NULL;
-- <<< DocumentsIndexesMigration

-- >>> DocumentRelationshipsMigration
                                CREATE TABLE IF NOT EXISTS document_relationships (
                                    relationship_id             uuid         PRIMARY KEY,

                                    from_document_id            uuid         NOT NULL,
                                    to_document_id              uuid         NOT NULL,

                                    relationship_code           text         NOT NULL,
                                    relationship_code_norm      text         GENERATED ALWAYS AS (lower(btrim(relationship_code))) STORED,

                                    created_at_utc              timestamptz  NOT NULL DEFAULT NOW()
                                );

                                -- Drift repair (columns)
                                ALTER TABLE document_relationships
                                    ADD COLUMN IF NOT EXISTS relationship_id uuid;

                                ALTER TABLE document_relationships
                                    ADD COLUMN IF NOT EXISTS from_document_id uuid;

                                ALTER TABLE document_relationships
                                    ADD COLUMN IF NOT EXISTS to_document_id uuid;

                                ALTER TABLE document_relationships
                                    ADD COLUMN IF NOT EXISTS relationship_code text;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'document_relationships'
                                          AND column_name = 'relationship_code_norm'
                                    ) THEN
                                        ALTER TABLE document_relationships
                                            ADD COLUMN relationship_code_norm text GENERATED ALWAYS AS (lower(btrim(relationship_code))) STORED;
                                    END IF;
                                END
                                $$;

                                ALTER TABLE document_relationships
                                    ADD COLUMN IF NOT EXISTS created_at_utc timestamptz;

                                -- Drift repair (PK)
                                DO $$
                                BEGIN
                                    -- NOTE:
                                    -- We intentionally avoid enforcing a specific PK constraint name.
                                    -- If the table was created with an inline PRIMARY KEY, PostgreSQL will
                                    -- name it e.g. document_relationships_pkey. Adding another PK would fail.
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint c
                                        JOIN pg_class t ON t.oid = c.conrelid
                                        JOIN pg_namespace n ON n.oid = t.relnamespace
                                        WHERE n.nspname = 'public'
                                          AND t.relname = 'document_relationships'
                                          AND c.contype = 'p'
                                    ) THEN
                                        ALTER TABLE document_relationships
                                            ADD CONSTRAINT pk_document_relationships PRIMARY KEY (relationship_id);
                                    END IF;
                                END
                                $$;

                                -- Constraints
                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_document_relationships_code_trimmed'
                                    ) THEN
                                        ALTER TABLE document_relationships
                                            ADD CONSTRAINT ck_document_relationships_code_trimmed
                                                CHECK (relationship_code = btrim(relationship_code));
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_document_relationships_code_nonempty'
                                    ) THEN
                                        ALTER TABLE document_relationships
                                            ADD CONSTRAINT ck_document_relationships_code_nonempty
                                                CHECK (length(relationship_code) > 0);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_document_relationships_code_len'
                                    ) THEN
                                        ALTER TABLE document_relationships
                                            ADD CONSTRAINT ck_document_relationships_code_len
                                                CHECK (length(relationship_code) <= 128);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_document_relationships_not_self'
                                    ) THEN
                                        ALTER TABLE document_relationships
                                            ADD CONSTRAINT ck_document_relationships_not_self
                                                CHECK (from_document_id <> to_document_id);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ux_document_relationships_triplet'
                                    ) THEN
                                        -- Note: PostgreSQL represents indexes as relations in the schema namespace.
                                        -- If a drift-repair test (or a manual DBA action) created a UNIQUE INDEX
                                        -- with the same contract name, attempting to ADD CONSTRAINT ... UNIQUE (...)
                                        -- would fail with 42P07 (relation already exists). To keep the migration
                                        -- strictly idempotent and drift-repairable, drop any conflicting index
                                        -- and recreate the constraint (which will re-create the index with the
                                        -- expected column order).
                                        IF EXISTS (
                                            SELECT 1
                                            FROM pg_class c
                                            JOIN pg_namespace n ON n.oid = c.relnamespace
                                            WHERE n.nspname = 'public'
                                              AND c.relkind = 'i'
                                              AND c.relname = 'ux_document_relationships_triplet'
                                        ) THEN
                                            DROP INDEX IF EXISTS public.ux_document_relationships_triplet;
                                        END IF;

                                        ALTER TABLE document_relationships
                                            ADD CONSTRAINT ux_document_relationships_triplet
                                                UNIQUE (from_document_id, relationship_code_norm, to_document_id);
                                    END IF;
                                END
                                $$;
                                -- FKs
                                --
                                -- Respawn resets data but not schema; drift tests can rename constraints.
                                -- We enforce the required contract names AND ON DELETE CASCADE semantics
                                -- by re-creating the FKs on every bootstrap (migrations are serialized).
                                ALTER TABLE document_relationships
                                    DROP CONSTRAINT IF EXISTS fk_docrel_from_document;

                                ALTER TABLE document_relationships
                                    DROP CONSTRAINT IF EXISTS fk_docrel_to_document;

                                ALTER TABLE document_relationships
                                    DROP CONSTRAINT IF EXISTS fk_document_relationships_from_document;

                                ALTER TABLE document_relationships
                                    DROP CONSTRAINT IF EXISTS fk_document_relationships_to_document;

                                ALTER TABLE document_relationships
                                    ADD CONSTRAINT fk_document_relationships_from_document
                                        FOREIGN KEY (from_document_id)
                                            REFERENCES documents(id)
                                            ON DELETE CASCADE;

                                ALTER TABLE document_relationships
                                    ADD CONSTRAINT fk_document_relationships_to_document
                                        FOREIGN KEY (to_document_id)
                                            REFERENCES documents(id)
                                            ON DELETE CASCADE;

                                -- Default value drift repair for created_at_utc.
                                DO $$
                                BEGIN
                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'document_relationships'
                                          AND column_name = 'created_at_utc'
                                    ) THEN
                                        ALTER TABLE document_relationships
                                            ALTER COLUMN created_at_utc SET DEFAULT NOW();
                                    END IF;
                                END
                                $$;
-- <<< DocumentRelationshipsMigration

-- >>> DocumentRelationshipsIndexesMigration
                                CREATE INDEX IF NOT EXISTS ix_docrel_from_created_id
                                    ON document_relationships (from_document_id, created_at_utc DESC, relationship_id DESC);

                                CREATE INDEX IF NOT EXISTS ix_docrel_to_created_id
                                    ON document_relationships (to_document_id, created_at_utc DESC, relationship_id DESC);

                                -- These are used for common reads like:
                                --   WHERE from_document_id = @id AND relationship_code_norm = @codeNorm
                                --   ORDER BY created_at_utc DESC, relationship_id DESC
                                CREATE INDEX IF NOT EXISTS ix_docrel_from_code_created_id
                                    ON document_relationships (from_document_id, relationship_code_norm, created_at_utc DESC, relationship_id DESC);

                                CREATE INDEX IF NOT EXISTS ix_docrel_to_code_created_id
                                    ON document_relationships (to_document_id, relationship_code_norm, created_at_utc DESC, relationship_id DESC);
-- <<< DocumentRelationshipsIndexesMigration

-- >>> DocumentRelationshipsCardinalityIndexesMigration
                                -- reversal_of: each document can be a reversal of at most one other document
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_docrel_from_rev_of
                                    ON document_relationships (from_document_id)
                                    WHERE relationship_code_norm = 'reversal_of';

                                -- created_from: each document can be created from at most one source document
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_docrel_from_created_from
                                    ON document_relationships (from_document_id)
                                    WHERE relationship_code_norm = 'created_from';

                                -- supersedes: one-to-one
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_docrel_from_supersedes
                                    ON document_relationships (from_document_id)
                                    WHERE relationship_code_norm = 'supersedes';

                                CREATE UNIQUE INDEX IF NOT EXISTS ux_docrel_to_supersedes
                                    ON document_relationships (to_document_id)
                                    WHERE relationship_code_norm = 'supersedes';
-- <<< DocumentRelationshipsCardinalityIndexesMigration

-- >>> DocumentRelationshipsDraftGuardMigration
                                CREATE OR REPLACE FUNCTION ngb_enforce_document_relationships_draft_from_document()
                                RETURNS trigger AS $$
                                DECLARE
                                    from_status smallint;
                                BEGIN
                                    -- Allow FK cascades (parent table trigger invokes DELETE on this table).
                                    -- pg_trigger_depth() > 1 reliably identifies such cases.
                                    IF TG_OP = 'DELETE' AND pg_trigger_depth() > 1 THEN
                                        RETURN OLD;
                                    END IF;

                                    IF TG_OP = 'UPDATE' THEN
                                        RAISE EXCEPTION 'Document relationships are immutable. Delete and recreate the edge.'
                                            USING ERRCODE = '55000';
                                    END IF;

                                    IF TG_OP = 'INSERT' THEN
                                        SELECT d.status INTO from_status
                                        FROM documents d
                                        WHERE d.id = NEW.from_document_id;

                                        -- 1 = DocumentStatus.Draft
                                        IF from_status IS DISTINCT FROM 1 THEN
                                            RAISE EXCEPTION 'Document relationships can only be mutated while the from-document is Draft.'
                                                USING ERRCODE = '55000';
                                        END IF;

                                        RETURN NEW;
                                    END IF;

                                    -- DELETE (direct statement)
                                    -- If either endpoint no longer exists, this is either a cleanup or a cascade path.
                                    -- We allow it to avoid blocking document deletions.
                                    IF NOT EXISTS (SELECT 1 FROM documents d WHERE d.id = OLD.from_document_id)
                                       OR NOT EXISTS (SELECT 1 FROM documents d WHERE d.id = OLD.to_document_id)
                                    THEN
                                        RETURN OLD;
                                    END IF;

                                    SELECT d.status INTO from_status
                                    FROM documents d
                                    WHERE d.id = OLD.from_document_id;

                                    IF from_status IS DISTINCT FROM 1 THEN
                                        RAISE EXCEPTION 'Document relationships can only be mutated while the from-document is Draft.'
                                            USING ERRCODE = '55000';
                                    END IF;

                                    RETURN OLD;
                                END;
                                $$ LANGUAGE plpgsql;

                                -- (Re)install trigger deterministically.
                                DROP TRIGGER IF EXISTS trg_document_relationships_draft_guard ON public.document_relationships;
                                CREATE TRIGGER trg_document_relationships_draft_guard
                                BEFORE INSERT OR UPDATE OR DELETE ON public.document_relationships
                                FOR EACH ROW
                                EXECUTE FUNCTION ngb_enforce_document_relationships_draft_from_document();
-- <<< DocumentRelationshipsDraftGuardMigration

-- >>> OperationalRegistersMigration
                                CREATE TABLE IF NOT EXISTS operational_registers (
                                    register_id UUID PRIMARY KEY,

                                    code TEXT NOT NULL,
                                    name TEXT NOT NULL,

                                    -- Once any movements have been written to a register, some metadata becomes immutable
                                    -- (e.g. resource physical column identifiers). This flag allows DB-level guards to
                                    -- enforce that invariant without scanning per-register tables.
                                    has_movements BOOLEAN NOT NULL DEFAULT FALSE,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT ck_operational_registers_code_nonempty CHECK (length(trim(code)) > 0),
                                    CONSTRAINT ck_operational_registers_name_nonempty CHECK (length(trim(name)) > 0)
                                );

                                -- Drift repair: CREATE TABLE IF NOT EXISTS won't add dropped columns / constraints.
                                -- Some drift-repair tests intentionally drop columns like register_id.
                                ALTER TABLE operational_registers
                                    ADD COLUMN IF NOT EXISTS register_id uuid,
                                    ADD COLUMN IF NOT EXISTS code text,
                                    ADD COLUMN IF NOT EXISTS name text,
                                    ADD COLUMN IF NOT EXISTS has_movements boolean NOT NULL DEFAULT FALSE,
                                    ADD COLUMN IF NOT EXISTS created_at_utc timestamptz NOT NULL DEFAULT NOW(),
                                    ADD COLUMN IF NOT EXISTS updated_at_utc timestamptz NOT NULL DEFAULT NOW();

                                DO $$
                                BEGIN
                                    -- Primary key (idempotent).
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_registers'::regclass
                                          AND contype = 'p'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_registers
                                                ADD CONSTRAINT pk_operational_registers
                                                    PRIMARY KEY (register_id)
                                        $ddl$;
                                    END IF;

                                    -- Non-empty constraints (idempotent).
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_registers'::regclass
                                          AND conname = 'ck_operational_registers_code_nonempty'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_registers
                                                ADD CONSTRAINT ck_operational_registers_code_nonempty
                                                    CHECK (length(trim(code)) > 0)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_registers'::regclass
                                          AND conname = 'ck_operational_registers_name_nonempty'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_registers
                                                ADD CONSTRAINT ck_operational_registers_name_nonempty
                                                    CHECK (length(trim(name)) > 0)
                                        $ddl$;
                                    END IF;

                                    -- Timestamp defaults (timestamptz instants use DEFAULT NOW()).
                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'operational_registers'
                                          AND column_name = 'created_at_utc'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_registers
                                                ALTER COLUMN created_at_utc SET DEFAULT NOW()
                                        $ddl$;
                                    END IF;

                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'operational_registers'
                                          AND column_name = 'updated_at_utc'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_registers
                                                ALTER COLUMN updated_at_utc SET DEFAULT NOW()
                                        $ddl$;
                                    END IF;
                                END$$;
-- <<< OperationalRegistersMigration

-- >>> OperationalRegistersCodeNormMigration
                                ALTER TABLE operational_registers
                                    ADD COLUMN IF NOT EXISTS code_norm TEXT GENERATED ALWAYS AS (lower(btrim(code))) STORED;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_operational_registers_code_trimmed'
                                    ) THEN
                                        ALTER TABLE operational_registers
                                            ADD CONSTRAINT ck_operational_registers_code_trimmed
                                            CHECK (code = btrim(code));
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_operational_registers_name_trimmed'
                                    ) THEN
                                        ALTER TABLE operational_registers
                                            ADD CONSTRAINT ck_operational_registers_name_trimmed
                                            CHECK (name = btrim(name));
                                    END IF;
                                END
                                $$;
-- <<< OperationalRegistersCodeNormMigration

-- >>> OperationalRegistersTableCodeMigration
                                ALTER TABLE operational_registers
                                    ADD COLUMN IF NOT EXISTS table_code TEXT GENERATED ALWAYS AS (
                                        (
                                            CASE
                                                WHEN length(
                                                    btrim(
                                                        regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                        '_'
                                                    )
                                                ) = 0 THEN ''

                                                WHEN length(
                                                    btrim(
                                                        regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                        '_'
                                                    )
                                                ) <= 46 THEN
                                                    btrim(
                                                        regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                        '_'
                                                    )

                                                ELSE
                                                    left(
                                                        btrim(
                                                            regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                            '_'
                                                        ),
                                                        33
                                                    )
                                                    || '_'
                                                    || substr(
                                                        md5(
                                                            btrim(
                                                                regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                                '_'
                                                            )
                                                        ),
                                                        1,
                                                        12
                                                    )
                                            END
                                        )
                                    ) STORED;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_operational_registers_table_code_nonempty'
                                    ) THEN
                                        ALTER TABLE operational_registers
                                            ADD CONSTRAINT ck_operational_registers_table_code_nonempty
                                            CHECK (length(table_code) > 0);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_operational_registers_table_code_safe'
                                    ) THEN
                                        ALTER TABLE operational_registers
                                            ADD CONSTRAINT ck_operational_registers_table_code_safe
                                            CHECK (table_code ~ '^[a-z0-9_]+$' AND length(table_code) > 0);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_operational_registers_table_code_len'
                                    ) THEN
                                        ALTER TABLE operational_registers
                                            ADD CONSTRAINT ck_operational_registers_table_code_len
                                            CHECK (length(table_code) <= 46);
                                    END IF;
                                END
                                $$;
-- <<< OperationalRegistersTableCodeMigration

-- >>> OperationalRegisterResourcesMigration
                                CREATE TABLE IF NOT EXISTS operational_register_resources
                                (
                                    register_id       uuid        NOT NULL,
                                    code              text        NOT NULL,
                                    code_norm         text        NOT NULL,
                                    column_code       text        NOT NULL,
                                    name              text        NOT NULL,
                                    ordinal           integer     NOT NULL,
                                    created_at_utc    timestamptz NOT NULL DEFAULT NOW(),
                                    updated_at_utc    timestamptz NOT NULL DEFAULT NOW(),

                                    CONSTRAINT pk_operational_register_resources
                                        PRIMARY KEY (register_id, column_code),

                                    CONSTRAINT ux_operational_register_resources__register_code_norm
                                        UNIQUE (register_id, code_norm),

                                    CONSTRAINT ux_operational_register_resources__register_ordinal
                                        UNIQUE (register_id, ordinal),

                                    CONSTRAINT ck_operational_register_resources__ordinal_positive
                                        CHECK (ordinal > 0),

                                    CONSTRAINT ck_operational_register_resources__code_norm
                                        CHECK (code_norm = lower(btrim(code))),

                                    -- column_code becomes an UNQUOTED SQL identifier in dynamic DDL/DML.
                                    -- PostgreSQL rules for unquoted identifiers: ^[a-z_][a-z0-9_]*$
                                    -- Identifier length limit is 63 bytes (ASCII => 63 chars).
                                    CONSTRAINT ck_operational_register_resources__column_code_safe
                                        CHECK (column_code ~ '^[a-z_][a-z0-9_]*$' AND length(column_code) > 0 AND length(column_code) <= 63),

                                    CONSTRAINT ck_operational_register_resources__column_code_not_reserved
                                        CHECK (column_code NOT IN ('movement_id','turnover_id','balance_id','document_id','occurred_at_utc','period_month','dimension_set_id','is_storno')),

                                    CONSTRAINT fk_opreg_resources__register
                                        FOREIGN KEY (register_id) REFERENCES operational_registers(register_id)
                                );

                                -- Drift repair: CREATE TABLE IF NOT EXISTS doesn't restore dropped columns.
                                ALTER TABLE operational_register_resources
                                    ADD COLUMN IF NOT EXISTS register_id uuid,
                                    ADD COLUMN IF NOT EXISTS code text,
                                    ADD COLUMN IF NOT EXISTS code_norm text,
                                    ADD COLUMN IF NOT EXISTS column_code text,
                                    ADD COLUMN IF NOT EXISTS name text,
                                    ADD COLUMN IF NOT EXISTS ordinal integer;


                                DO $$
                                BEGIN
                                    -- Drift repair: ensure required columns exist before constraints.
                                    -- (Some test cases drop columns; keeping this inside the DO block avoids multi-statement parse/plan ordering issues.)
                                    EXECUTE $ddl$
                                        ALTER TABLE operational_register_resources
                                            ADD COLUMN IF NOT EXISTS register_id uuid,
                                            ADD COLUMN IF NOT EXISTS code text,
                                            ADD COLUMN IF NOT EXISTS code_norm text,
                                            ADD COLUMN IF NOT EXISTS column_code text,
                                            ADD COLUMN IF NOT EXISTS name text,
                                            ADD COLUMN IF NOT EXISTS ordinal integer
                                    $ddl$;

                                    -- Drift-repair: CREATE TABLE IF NOT EXISTS will not re-create dropped constraints.

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_resources'::regclass
                                          AND contype = 'p'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ADD CONSTRAINT pk_operational_register_resources
                                                    PRIMARY KEY (register_id, column_code)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_resources'::regclass
                                          AND conname = 'ux_operational_register_resources__register_code_norm'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ADD CONSTRAINT ux_operational_register_resources__register_code_norm
                                                    UNIQUE (register_id, code_norm)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_resources'::regclass
                                          AND conname = 'ux_operational_register_resources__register_ordinal'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ADD CONSTRAINT ux_operational_register_resources__register_ordinal
                                                    UNIQUE (register_id, ordinal)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_resources'::regclass
                                          AND conname = 'fk_opreg_resources__register'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ADD CONSTRAINT fk_opreg_resources__register
                                                    FOREIGN KEY (register_id)
                                                    REFERENCES operational_registers(register_id)
                                        $ddl$;
                                    END IF;
                                END$$;

                                -- Drift repair for timestamp defaults (timestamptz instants use DEFAULT NOW()).
                                DO $$
                                BEGIN
                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'operational_register_resources'
                                          AND column_name = 'created_at_utc'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ALTER COLUMN created_at_utc SET DEFAULT NOW()
                                        $ddl$;
                                    END IF;

                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'operational_register_resources'
                                          AND column_name = 'updated_at_utc'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ALTER COLUMN updated_at_utc SET DEFAULT NOW()
                                        $ddl$;
                                    END IF;
                                END$$;

                                -- Drift repair for ck_operational_register_resources__column_code_safe.
                                DO $$
                                DECLARE
                                    def text;
                                BEGIN
                                    SELECT pg_get_constraintdef(c.oid)
                                      INTO def
                                      FROM pg_constraint c
                                      JOIN pg_class t ON t.oid = c.conrelid
                                      JOIN pg_namespace n ON n.oid = t.relnamespace
                                     WHERE n.nspname = 'public'
                                       AND t.relname = 'operational_register_resources'
                                       AND c.conname = 'ck_operational_register_resources__column_code_safe';

                                    IF def IS NULL THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ADD CONSTRAINT ck_operational_register_resources__column_code_safe
                                                    CHECK (column_code ~ '^[a-z_][a-z0-9_]*$' AND length(column_code) > 0 AND length(column_code) <= 63)
                                        $ddl$;
                                    ELSIF def NOT LIKE '%^[a-z_][a-z0-9_]*$%'
                                       OR def NOT LIKE '%<= 63%'
                                       OR def NOT LIKE '%length(column_code) > 0%'
                                    THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                DROP CONSTRAINT ck_operational_register_resources__column_code_safe
                                        $ddl$;

                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ADD CONSTRAINT ck_operational_register_resources__column_code_safe
                                                    CHECK (column_code ~ '^[a-z_][a-z0-9_]*$' AND length(column_code) > 0 AND length(column_code) <= 63)
                                        $ddl$;
                                    END IF;

                                    -- Drift repair for ck_operational_register_resources__column_code_not_reserved.
                                    SELECT pg_get_constraintdef(c.oid)
                                      INTO def
                                      FROM pg_constraint c
                                      JOIN pg_class t ON t.oid = c.conrelid
                                      JOIN pg_namespace n ON n.oid = t.relnamespace
                                     WHERE n.nspname = 'public'
                                       AND t.relname = 'operational_register_resources'
                                       AND c.conname = 'ck_operational_register_resources__column_code_not_reserved';

                                    IF def IS NULL THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_resources
                                                ADD CONSTRAINT ck_operational_register_resources__column_code_not_reserved
                                                    CHECK (column_code NOT IN ('movement_id','turnover_id','balance_id','document_id','occurred_at_utc','period_month','dimension_set_id','is_storno'))
                                        $ddl$;
                                    END IF;
                                END$$;

                                -- DB-level immutability guard: once a register has movements, resource identifiers become immutable.
                                -- We allow updating only user-facing fields (name/ordinal) but forbid:
                                -- - DELETE
                                -- - changing code/code_norm/column_code
                                -- This protects reversal/storno semantics and dynamic schema safety even if callers bypass runtime services.
                                CREATE OR REPLACE FUNCTION ngb_opreg_forbid_resource_mutation_when_has_movements()
                                RETURNS trigger AS $$
                                DECLARE
                                    has_mov boolean;
                                BEGIN
                                    SELECT r.has_movements
                                      INTO has_mov
                                      FROM operational_registers r
                                     WHERE r.register_id = OLD.register_id;

                                    IF COALESCE(has_mov, FALSE) THEN
                                        IF TG_OP = 'DELETE' THEN
                                            RAISE EXCEPTION 'Operational register resources are immutable after movements exist.';
                                        END IF;

                                        IF TG_OP = 'UPDATE' THEN
                                            IF NEW.register_id IS DISTINCT FROM OLD.register_id
                                               OR NEW.code IS DISTINCT FROM OLD.code
                                               OR NEW.code_norm IS DISTINCT FROM OLD.code_norm
                                               OR NEW.column_code IS DISTINCT FROM OLD.column_code
                                            THEN
                                                RAISE EXCEPTION 'Operational register resource identifiers are immutable after movements exist.';
                                            END IF;
                                        END IF;
                                    END IF;

                                    IF TG_OP = 'DELETE' THEN
                                        RETURN OLD;
                                    END IF;

                                    RETURN NEW;
                                END;
                                $$ LANGUAGE plpgsql;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_trigger
                                        WHERE tgname = 'trg_opreg_resources_immutable_when_has_movements'
                                          AND tgrelid = 'operational_register_resources'::regclass
                                    ) THEN
                                        CREATE TRIGGER trg_opreg_resources_immutable_when_has_movements
                                            BEFORE UPDATE OR DELETE
                                            ON operational_register_resources
                                            FOR EACH ROW
                                            EXECUTE FUNCTION ngb_opreg_forbid_resource_mutation_when_has_movements();
                                    END IF;
                                END$$;
-- <<< OperationalRegisterResourcesMigration

-- >>> OperationalRegisterDimensionRulesMigration
                                CREATE TABLE IF NOT EXISTS operational_register_dimension_rules (
                                    register_id UUID NOT NULL,
                                    dimension_id UUID NOT NULL,

                                    ordinal INT4 NOT NULL,
                                    is_required BOOLEAN NOT NULL DEFAULT FALSE,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT pk_opreg_dim_rules PRIMARY KEY (register_id, dimension_id),

                                    CONSTRAINT ux_opreg_dim_rules__register_ordinal
                                        UNIQUE (register_id, ordinal),

                                    CONSTRAINT fk_opreg_dim_rules_register
                                        FOREIGN KEY (register_id)
                                        REFERENCES operational_registers(register_id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT fk_opreg_dim_rules_dimension
                                        FOREIGN KEY (dimension_id)
                                        REFERENCES platform_dimensions(dimension_id),

                                    CONSTRAINT ck_opreg_dim_rules_ordinal_positive CHECK (ordinal > 0)
                                );

                                -- Drift repair: CREATE TABLE IF NOT EXISTS doesn't restore dropped columns.
                                ALTER TABLE operational_register_dimension_rules
                                    ADD COLUMN IF NOT EXISTS register_id uuid,
                                    ADD COLUMN IF NOT EXISTS dimension_id uuid,
                                    ADD COLUMN IF NOT EXISTS ordinal int4,
                                    ADD COLUMN IF NOT EXISTS is_required boolean;


                                DO $$
                                BEGIN
                                    -- Drift repair: ensure required columns exist before constraints.
                                    -- (Some test cases drop columns; keeping this inside the DO block avoids multi-statement parse/plan ordering issues.)
                                    EXECUTE $ddl$
                                        ALTER TABLE operational_register_dimension_rules
                                            ADD COLUMN IF NOT EXISTS register_id uuid,
                                            ADD COLUMN IF NOT EXISTS dimension_id uuid,
                                            ADD COLUMN IF NOT EXISTS ordinal int4,
                                            ADD COLUMN IF NOT EXISTS is_required boolean
                                    $ddl$;

                                    -- Drift-repair: CREATE TABLE IF NOT EXISTS will not re-create dropped constraints.

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_dimension_rules'::regclass
                                          AND contype = 'p'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_dimension_rules
                                                ADD CONSTRAINT pk_opreg_dim_rules PRIMARY KEY (register_id, dimension_id)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_dimension_rules'::regclass
                                          AND conname = 'ux_opreg_dim_rules__register_ordinal'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_dimension_rules
                                                ADD CONSTRAINT ux_opreg_dim_rules__register_ordinal
                                                    UNIQUE (register_id, ordinal)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_dimension_rules'::regclass
                                          AND conname = 'fk_opreg_dim_rules_register'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_dimension_rules
                                                ADD CONSTRAINT fk_opreg_dim_rules_register
                                                    FOREIGN KEY (register_id)
                                                    REFERENCES operational_registers(register_id)
                                                    ON DELETE CASCADE
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_dimension_rules'::regclass
                                          AND conname = 'fk_opreg_dim_rules_dimension'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_dimension_rules
                                                ADD CONSTRAINT fk_opreg_dim_rules_dimension
                                                    FOREIGN KEY (dimension_id)
                                                    REFERENCES platform_dimensions(dimension_id)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_dimension_rules'::regclass
                                          AND conname = 'ck_opreg_dim_rules_ordinal_positive'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_dimension_rules
                                                ADD CONSTRAINT ck_opreg_dim_rules_ordinal_positive
                                                    CHECK (ordinal > 0)
                                        $ddl$;
                                    END IF;
                                END
                                $$;
-- <<< OperationalRegisterDimensionRulesMigration

-- >>> OperationalRegisterExtraGuardsMigration
                       -- Guard: prevent dangerous registry mutations after movements exist.
                       CREATE OR REPLACE FUNCTION ngb_opreg_forbid_register_mutation_when_has_movements()
                       RETURNS trigger AS $$
                       BEGIN
                           IF COALESCE(OLD.has_movements, FALSE) THEN
                               IF TG_OP = 'DELETE' THEN
                                   RAISE EXCEPTION 'Operational register metadata is immutable after movements exist.';
                               END IF;

                               IF TG_OP = 'UPDATE' THEN
                                   -- code drives generated code_norm/table_code (and thus dynamic per-register table names)
                                   -- NOTE: compare only the base column. Generated columns may be recomputed after BEFORE triggers.
                                   IF NEW.register_id IS DISTINCT FROM OLD.register_id
                                      OR NEW.code IS DISTINCT FROM OLD.code
                                   THEN
                                       RAISE EXCEPTION 'Operational register code is immutable after movements exist.';
                                   END IF;

                                   IF OLD.has_movements = TRUE AND NEW.has_movements = FALSE THEN
                                       RAISE EXCEPTION 'Operational register has_movements can never flip back to FALSE.';
                                   END IF;
                               END IF;
                           END IF;

                           IF TG_OP = 'DELETE' THEN
                               RETURN OLD;
                           END IF;

                           RETURN NEW;
                       END;
                       $$ LANGUAGE plpgsql;
                       

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_trigger
        WHERE tgname = 'trg_opreg_registers_immutable_when_has_movements'
          AND tgrelid = 'operational_registers'::regclass
    ) THEN
        CREATE TRIGGER trg_opreg_registers_immutable_when_has_movements
            BEFORE UPDATE OR DELETE
            ON operational_registers
            FOR EACH ROW
            EXECUTE FUNCTION ngb_opreg_forbid_register_mutation_when_has_movements();
    END IF;
END$$;

                       -- Guard: protect dimension rules from destructive changes after movements exist.
                       CREATE OR REPLACE FUNCTION ngb_opreg_forbid_dim_rule_mutation_when_has_movements()
                       RETURNS trigger AS $$
                       DECLARE
                           has_mov boolean;
                           reg_id uuid;
                       BEGIN
                           reg_id := COALESCE(NEW.register_id, OLD.register_id);

                           SELECT r.has_movements
                             INTO has_mov
                             FROM operational_registers r
                            WHERE r.register_id = reg_id;

                           IF COALESCE(has_mov, FALSE) THEN
                               IF TG_OP = 'DELETE' THEN
                                   RAISE EXCEPTION 'Operational register dimension rules are immutable after movements exist.';
                               END IF;

                               IF TG_OP = 'INSERT' THEN
                                   -- Adding an optional dimension rule is allowed for forward-only evolution,
                                   -- but adding a required rule would invalidate historical movements.
                                   IF COALESCE(NEW.is_required, FALSE) THEN
                                       RAISE EXCEPTION 'Cannot add a required dimension rule after movements exist.';
                                   END IF;
                               END IF;

                               IF TG_OP = 'UPDATE' THEN
                                   RAISE EXCEPTION 'Operational register dimension rules are append-only after movements exist.';
                               END IF;
                           END IF;

                           IF TG_OP = 'DELETE' THEN
                               RETURN OLD;
                           END IF;

                           RETURN NEW;
                       END;
                       $$ LANGUAGE plpgsql;
                       

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_trigger
        WHERE tgname = 'trg_opreg_dim_rules_immutable_when_has_movements'
          AND tgrelid = 'operational_register_dimension_rules'::regclass
    ) THEN
        CREATE TRIGGER trg_opreg_dim_rules_immutable_when_has_movements
            BEFORE INSERT OR UPDATE OR DELETE
            ON operational_register_dimension_rules
            FOR EACH ROW
            EXECUTE FUNCTION ngb_opreg_forbid_dim_rule_mutation_when_has_movements();
    END IF;
END$$;
-- <<< OperationalRegisterExtraGuardsMigration

-- >>> OperationalRegisterFinalizationsMigration
                                CREATE TABLE IF NOT EXISTS operational_register_finalizations (
                                    register_id UUID NOT NULL,
                                    period DATE NOT NULL,

                                    status SMALLINT NOT NULL,
                                    finalized_at_utc TIMESTAMPTZ NULL,
                                    dirty_since_utc TIMESTAMPTZ NULL,
                                    blocked_since_utc TIMESTAMPTZ NULL,
                                    blocked_reason TEXT NULL,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT pk_opreg_finalizations PRIMARY KEY (register_id, period),

                                    CONSTRAINT fk_opreg_finalizations_register
                                        FOREIGN KEY (register_id)
                                        REFERENCES operational_registers(register_id)
                                        ON DELETE CASCADE,

                                    -- 1 = Finalized, 2 = Dirty, 3 = BlockedNoProjector
                                    CONSTRAINT ck_opreg_finalizations_status
                                        CHECK (status IN (1, 2, 3)),

                                    -- month start invariant
                                    CONSTRAINT ck_opreg_finalizations_period_is_month_start
                                        CHECK (date_trunc('month', period::timestamp)::date = period),

                                    CONSTRAINT ck_opreg_finalizations_consistent_timestamps
                                        CHECK (
                                            (status = 1 AND finalized_at_utc IS NOT NULL AND dirty_since_utc IS NULL AND blocked_since_utc IS NULL AND blocked_reason IS NULL)
                                            OR
                                            (status = 2 AND dirty_since_utc IS NOT NULL AND finalized_at_utc IS NULL AND blocked_since_utc IS NULL AND blocked_reason IS NULL)
                                            OR
                                            (status = 3 AND blocked_since_utc IS NOT NULL AND blocked_reason IS NOT NULL AND finalized_at_utc IS NULL AND dirty_since_utc IS NULL)
                                        )
                                );

                                -- Drift repair: CREATE TABLE IF NOT EXISTS doesn't restore dropped columns.
                                ALTER TABLE operational_register_finalizations
                                    ADD COLUMN IF NOT EXISTS register_id uuid,
                                    ADD COLUMN IF NOT EXISTS period date,
                                    ADD COLUMN IF NOT EXISTS status smallint;


                                DO $$
                                BEGIN
                                    -- Drift repair: ensure required columns exist before constraints.
                                    -- (Some test cases drop columns; keeping this inside the DO block avoids multi-statement parse/plan ordering issues.)
                                    EXECUTE $ddl$
                                        ALTER TABLE operational_register_finalizations
                                            ADD COLUMN IF NOT EXISTS register_id uuid,
                                            ADD COLUMN IF NOT EXISTS period date,
                                            ADD COLUMN IF NOT EXISTS status smallint
                                    $ddl$;

                                    -- Drift-repair: CREATE TABLE IF NOT EXISTS will not re-create dropped constraints.

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_finalizations'::regclass
                                          AND contype = 'p'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_finalizations
                                                ADD CONSTRAINT pk_opreg_finalizations PRIMARY KEY (register_id, period)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_finalizations'::regclass
                                          AND conname = 'fk_opreg_finalizations_register'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_finalizations
                                                ADD CONSTRAINT fk_opreg_finalizations_register
                                                    FOREIGN KEY (register_id)
                                                    REFERENCES operational_registers(register_id)
                                                    ON DELETE CASCADE
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_finalizations'::regclass
                                          AND conname = 'ck_opreg_finalizations_status'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_finalizations
                                                ADD CONSTRAINT ck_opreg_finalizations_status
                                                    CHECK (status IN (1, 2, 3))
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_finalizations'::regclass
                                          AND conname = 'ck_opreg_finalizations_period_is_month_start'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_finalizations
                                                ADD CONSTRAINT ck_opreg_finalizations_period_is_month_start
                                                    CHECK (date_trunc('month', period::timestamp)::date = period)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_finalizations'::regclass
                                          AND conname = 'ck_opreg_finalizations_consistent_timestamps'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_finalizations
                                                ADD CONSTRAINT ck_opreg_finalizations_consistent_timestamps
                                                    CHECK (
                                                        (status = 1 AND finalized_at_utc IS NOT NULL AND dirty_since_utc IS NULL AND blocked_since_utc IS NULL AND blocked_reason IS NULL)
                                                        OR
                                                        (status = 2 AND dirty_since_utc IS NOT NULL AND finalized_at_utc IS NULL AND blocked_since_utc IS NULL AND blocked_reason IS NULL)
                                                        OR
                                                        (status = 3 AND blocked_since_utc IS NOT NULL AND blocked_reason IS NOT NULL AND finalized_at_utc IS NULL AND dirty_since_utc IS NULL)
                                                    )
                                        $ddl$;
                                    END IF;
                                END
                                $$;
-- <<< OperationalRegisterFinalizationsMigration

-- >>> OperationalRegisterWriteStateMigration
                                DO $$
                                BEGIN
                                    IF to_regclass('public.operational_register_write_state') IS NULL
                                       AND to_regclass('public.operational_register_write_log') IS NOT NULL THEN
                                        ALTER TABLE operational_register_write_log RENAME TO operational_register_write_state;
                                    END IF;
                                END
                                $$;

                                CREATE TABLE IF NOT EXISTS operational_register_write_state (
                                    register_id      uuid         NOT NULL,
                                    document_id      uuid         NOT NULL,
                                    operation        smallint     NOT NULL,

                                    attempt_id       uuid         NULL,
                                    started_at_utc   timestamptz  NOT NULL,
                                    completed_at_utc timestamptz  NULL,

                                    CONSTRAINT pk_operational_register_write_state PRIMARY KEY (register_id, document_id, operation),

                                    CONSTRAINT fk_opreg_write_log_register
                                        FOREIGN KEY (register_id)
                                        REFERENCES operational_registers(register_id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT fk_opreg_write_log_document
                                        FOREIGN KEY (document_id)
                                        REFERENCES documents(id)
                                        ON DELETE CASCADE,

                                    -- Allowed operations (mirrors PostingOperation used in accounting):
                                    -- 1 = Post, 2 = Unpost, 3 = Repost
                                    CONSTRAINT ck_opreg_write_log_operation
                                        CHECK (operation IN (1, 2, 3)),

                                    CONSTRAINT ck_opreg_write_log_completed_after_started
                                        CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)
                                );

                                ALTER TABLE operational_register_write_state
                                    ADD COLUMN IF NOT EXISTS attempt_id uuid;

                                -- Drift repair: CREATE TABLE IF NOT EXISTS doesn't restore dropped columns.
                                ALTER TABLE operational_register_write_state
                                    ADD COLUMN IF NOT EXISTS register_id uuid,
                                    ADD COLUMN IF NOT EXISTS document_id uuid,
                                    ADD COLUMN IF NOT EXISTS operation smallint,
                                    ADD COLUMN IF NOT EXISTS started_at_utc timestamptz,
                                    ADD COLUMN IF NOT EXISTS completed_at_utc timestamptz;


                                DO $$
                                BEGIN
                                    -- Drift repair: ensure required columns exist before constraints.
                                    -- (Some test cases drop columns; keeping this inside the DO block avoids multi-statement parse/plan ordering issues.)
                                    EXECUTE $ddl$
                                        ALTER TABLE operational_register_write_state
                                            ADD COLUMN IF NOT EXISTS register_id uuid,
                                            ADD COLUMN IF NOT EXISTS document_id uuid,
                                            ADD COLUMN IF NOT EXISTS operation smallint,
                                            ADD COLUMN IF NOT EXISTS attempt_id uuid,
                                            ADD COLUMN IF NOT EXISTS started_at_utc timestamptz,
                                            ADD COLUMN IF NOT EXISTS completed_at_utc timestamptz
                                    $ddl$;

                                    -- Drift-repair: CREATE TABLE IF NOT EXISTS will not re-create dropped constraints.

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_write_state'::regclass
                                          AND contype = 'p'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_write_state
                                                ADD CONSTRAINT pk_operational_register_write_state PRIMARY KEY (register_id, document_id, operation)
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_write_state'::regclass
                                          AND conname = 'fk_opreg_write_log_register'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_write_state
                                                ADD CONSTRAINT fk_opreg_write_log_register
                                                    FOREIGN KEY (register_id)
                                                    REFERENCES operational_registers(register_id)
                                                    ON DELETE CASCADE
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_write_state'::regclass
                                          AND conname = 'fk_opreg_write_log_document'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_write_state
                                                ADD CONSTRAINT fk_opreg_write_log_document
                                                    FOREIGN KEY (document_id)
                                                    REFERENCES documents(id)
                                                    ON DELETE CASCADE
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_write_state'::regclass
                                          AND conname = 'ck_opreg_write_log_operation'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_write_state
                                                ADD CONSTRAINT ck_opreg_write_log_operation
                                                    CHECK (operation IN (1, 2, 3))
                                        $ddl$;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'operational_register_write_state'::regclass
                                          AND conname = 'ck_opreg_write_log_completed_after_started'
                                    ) THEN
                                        EXECUTE $ddl$
                                            ALTER TABLE operational_register_write_state
                                                ADD CONSTRAINT ck_opreg_write_log_completed_after_started
                                                    CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)
                                        $ddl$;
                                    END IF;
                                END
                                $$;
-- <<< OperationalRegisterWriteStateMigration

-- >>> OperationalRegisterWriteLogHistoryMigration
                                CREATE TABLE IF NOT EXISTS operational_register_write_log_history (
                                    history_id       uuid         PRIMARY KEY,
                                    attempt_id       uuid         NOT NULL,
                                    register_id      uuid         NOT NULL,
                                    document_id      uuid         NOT NULL,
                                    operation        smallint     NOT NULL,
                                    event_kind       smallint     NOT NULL,
                                    occurred_at_utc  timestamptz  NOT NULL,

                                    CONSTRAINT fk_opreg_write_log_history_register
                                        FOREIGN KEY (register_id)
                                        REFERENCES operational_registers(register_id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT fk_opreg_write_log_history_document
                                        FOREIGN KEY (document_id)
                                        REFERENCES documents(id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT ck_opreg_write_log_history_operation
                                        CHECK (operation IN (1, 2, 3)),

                                    CONSTRAINT ck_opreg_write_log_history_event_kind
                                        CHECK (event_kind IN (1, 2, 3))
                                );

                                CREATE INDEX IF NOT EXISTS ix_opreg_write_log_history_document_operation_occurred
                                    ON operational_register_write_log_history(document_id, operation, occurred_at_utc DESC);

                                CREATE INDEX IF NOT EXISTS ix_opreg_write_log_history_attempt
                                    ON operational_register_write_log_history(attempt_id, occurred_at_utc);

                                DO $$
                                BEGIN
                                    IF to_regclass('public.operational_register_write_log_history') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_opreg_write_log_history_append_only ON public.operational_register_write_log_history;
                                        CREATE TRIGGER trg_opreg_write_log_history_append_only
                                            BEFORE UPDATE OR DELETE ON public.operational_register_write_log_history
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;
                                END
                                $$;
-- <<< OperationalRegisterWriteLogHistoryMigration

-- >>> OperationalRegistersIndexesMigration
                                -- registry
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_operational_registers_code_norm
                                    ON operational_registers(code_norm);

                                -- physical per-register tables are derived from table_code (see OperationalRegisterNaming)
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_operational_registers_table_code
                                    ON operational_registers(table_code);

                                -- resources (metadata -> physical column schema)
                                CREATE INDEX IF NOT EXISTS ix_opreg_resources_register_ordinal
                                    ON operational_register_resources(register_id, ordinal, code_norm);

                                -- dimension rules
                                CREATE INDEX IF NOT EXISTS ix_opreg_dim_rules_register_ordinal
                                    ON operational_register_dimension_rules(register_id, ordinal);

                                -- finalizations
                                CREATE INDEX IF NOT EXISTS ix_opreg_finalizations_register_period
                                    ON operational_register_finalizations(register_id, period);

                                -- write log
                                CREATE INDEX IF NOT EXISTS ix_opreg_write_log_document
                                    ON operational_register_write_state(document_id);
-- <<< OperationalRegistersIndexesMigration

-- >>> ReferenceRegistersMigration
                                CREATE TABLE IF NOT EXISTS reference_registers (
                                    register_id UUID PRIMARY KEY,

                                    code TEXT NOT NULL,
                                    name TEXT NOT NULL,

                                    -- 0 = NonPeriodic, 1 = Second, 2 = Day, 3 = Month, 4 = Quarter, 5 = Year
                                    periodicity SMALLINT NOT NULL,

                                    -- 0 = Independent, 1 = SubordinateToRecorder
                                    record_mode SMALLINT NOT NULL,

                                    -- Once any records exist, some metadata becomes immutable.
                                    has_records BOOLEAN NOT NULL DEFAULT FALSE,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT ck_reference_registers_code_nonempty CHECK (length(btrim(code)) > 0),
                                    CONSTRAINT ck_reference_registers_name_nonempty CHECK (length(btrim(name)) > 0),

                                    CONSTRAINT ck_reference_registers_periodicity CHECK (periodicity IN (0, 1, 2, 3, 4, 5)),
                                    CONSTRAINT ck_reference_registers_record_mode CHECK (record_mode IN (0, 1))
                                );

                                -- Drift repair: CREATE TABLE IF NOT EXISTS won't add new columns.
                                ALTER TABLE reference_registers
                                    ADD COLUMN IF NOT EXISTS has_records BOOLEAN NOT NULL DEFAULT FALSE;
-- <<< ReferenceRegistersMigration

-- >>> ReferenceRegistersCodeNormMigration
                                ALTER TABLE reference_registers
                                    ADD COLUMN IF NOT EXISTS code_norm TEXT GENERATED ALWAYS AS (lower(btrim(code))) STORED;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_reference_registers_code_trimmed'
                                    ) THEN
                                        ALTER TABLE reference_registers
                                            ADD CONSTRAINT ck_reference_registers_code_trimmed
                                            CHECK (code = btrim(code));
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_reference_registers_name_trimmed'
                                    ) THEN
                                        ALTER TABLE reference_registers
                                            ADD CONSTRAINT ck_reference_registers_name_trimmed
                                            CHECK (name = btrim(name));
                                    END IF;
                                END
                                $$;
-- <<< ReferenceRegistersCodeNormMigration

-- >>> ReferenceRegistersTableCodeMigration
                                ALTER TABLE reference_registers
                                    ADD COLUMN IF NOT EXISTS table_code TEXT GENERATED ALWAYS AS (
                                        (
                                            CASE
                                                WHEN length(
                                                    btrim(
                                                        regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                        '_'
                                                    )
                                                ) = 0 THEN ''

                                                WHEN length(
                                                    btrim(
                                                        regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                        '_'
                                                    )
                                                ) <= 47 THEN
                                                    btrim(
                                                        regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                        '_'
                                                    )

                                                ELSE
                                                    left(
                                                        btrim(
                                                            regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                            '_'
                                                        ),
                                                        34
                                                    )
                                                    || '_'
                                                    || substr(
                                                        md5(
                                                            btrim(
                                                                regexp_replace(lower(btrim(code)), '[^a-z0-9]+', '_', 'g'),
                                                                '_'
                                                            )
                                                        ),
                                                        1,
                                                        12
                                                    )
                                            END
                                        )
                                    ) STORED;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_reference_registers_table_code_nonempty'
                                    ) THEN
                                        ALTER TABLE reference_registers
                                            ADD CONSTRAINT ck_reference_registers_table_code_nonempty
                                            CHECK (length(table_code) > 0);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_reference_registers_table_code_safe'
                                    ) THEN
                                        ALTER TABLE reference_registers
                                            ADD CONSTRAINT ck_reference_registers_table_code_safe
                                            CHECK (table_code ~ '^[a-z0-9_]+$' AND length(table_code) > 0);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_reference_registers_table_code_len'
                                    ) THEN
                                        ALTER TABLE reference_registers
                                            ADD CONSTRAINT ck_reference_registers_table_code_len
                                            CHECK (length(table_code) <= 47);
                                    END IF;
                                END
                                $$;
-- <<< ReferenceRegistersTableCodeMigration

-- >>> ReferenceRegisterFieldsMigration
                                CREATE TABLE IF NOT EXISTS reference_register_fields
                                (
                                    register_id       uuid        NOT NULL,
                                    code              text        NOT NULL,
                                    code_norm         text        NOT NULL,
                                    column_code       text        NOT NULL,
                                    name              text        NOT NULL,
                                    ordinal           integer     NOT NULL,
                                    column_type       smallint    NOT NULL,
                                    is_nullable       boolean     NOT NULL,
                                    created_at_utc    timestamptz NOT NULL DEFAULT NOW(),
                                    updated_at_utc    timestamptz NOT NULL DEFAULT NOW(),

                                    CONSTRAINT pk_reference_register_fields
                                        PRIMARY KEY (register_id, column_code),

                                    CONSTRAINT ux_reference_register_fields__register_code_norm
                                        UNIQUE (register_id, code_norm),

                                    CONSTRAINT ux_reference_register_fields__register_ordinal
                                        UNIQUE (register_id, ordinal),

                                    CONSTRAINT ck_reference_register_fields__ordinal_positive
                                        CHECK (ordinal > 0),

                                    CONSTRAINT ck_reference_register_fields__code_norm
                                        CHECK (code_norm = lower(btrim(code))),

                                    -- column_code becomes an UNQUOTED SQL identifier in dynamic DDL/DML.
                                    -- PostgreSQL rules for unquoted identifiers: ^[a-z_][a-z0-9_]*$
                                    -- Identifier length limit is 63 bytes (ASCII => 63 chars).
                                    CONSTRAINT ck_reference_register_fields__column_code_safe
                                        CHECK (column_code ~ '^[a-z_][a-z0-9_]*$' AND length(column_code) > 0 AND length(column_code) <= 63),

                                    CONSTRAINT ck_reference_register_fields__column_code_not_reserved
                                        CHECK (column_code NOT IN ('record_id','period_utc','period_bucket_utc','dimension_set_id','recorder_document_id','recorded_at_utc','is_deleted','occurred_at_utc')),

                                    -- Allowed logical column types (mirrors NGB.Metadata.Base.ColumnType).
                                    CONSTRAINT ck_reference_register_fields__column_type
                                        CHECK (column_type IN (0, 1, 2, 3, 4, 5, 6, 7, 8)),

                                    CONSTRAINT fk_refreg_fields__register
                                        FOREIGN KEY (register_id) REFERENCES reference_registers(register_id)
                                );

                                -- Drift repair for timestamp defaults.
                                DO $$
                                BEGIN
                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'reference_register_fields'
                                          AND column_name = 'created_at_utc'
                                    ) THEN
                                        ALTER TABLE reference_register_fields
                                            ALTER COLUMN created_at_utc SET DEFAULT NOW();
                                    END IF;

                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'reference_register_fields'
                                          AND column_name = 'updated_at_utc'
                                    ) THEN
                                        ALTER TABLE reference_register_fields
                                            ALTER COLUMN updated_at_utc SET DEFAULT NOW();
                                    END IF;
                                END$$;

                                -- Drift repair for ck_reference_register_fields__column_code_not_reserved.
                                DO $$
                                DECLARE
                                    def text;
                                BEGIN
                                    SELECT pg_get_constraintdef(c.oid)
                                      INTO def
                                      FROM pg_constraint c
                                      JOIN pg_class t ON t.oid = c.conrelid
                                      JOIN pg_namespace n ON n.oid = t.relnamespace
                                     WHERE n.nspname = 'public'
                                       AND t.relname = 'reference_register_fields'
                                       AND c.conname = 'ck_reference_register_fields__column_code_not_reserved';

                                    IF def IS NULL THEN
                                        ALTER TABLE reference_register_fields
                                            ADD CONSTRAINT ck_reference_register_fields__column_code_not_reserved
                                            CHECK (column_code NOT IN ('record_id','period_utc','period_bucket_utc','dimension_set_id','recorder_document_id','recorded_at_utc','is_deleted','occurred_at_utc'));
                                    END IF;
                                END$$;

                                -- DB-level immutability guard: once a register has records, field identifiers become immutable.
                                -- We allow updating only user-facing fields (name/ordinal) but forbid:
                                -- - DELETE
                                -- - changing code/code_norm/column_code/column_type/is_nullable
                                CREATE OR REPLACE FUNCTION ngb_refreg_forbid_field_mutation_when_has_records()
                                RETURNS trigger AS $$
                                DECLARE
                                    has_rec boolean;
                                BEGIN
                                    SELECT r.has_records
                                      INTO has_rec
                                      FROM reference_registers r
                                     WHERE r.register_id = OLD.register_id;

                                    IF COALESCE(has_rec, FALSE) THEN
                                        IF TG_OP = 'DELETE' THEN
                                            RAISE EXCEPTION 'Reference register fields are immutable after records exist.';
                                        END IF;

                                        IF TG_OP = 'UPDATE' THEN
                                            IF NEW.register_id IS DISTINCT FROM OLD.register_id
                                               OR NEW.code IS DISTINCT FROM OLD.code
                                               OR NEW.code_norm IS DISTINCT FROM OLD.code_norm
                                               OR NEW.column_code IS DISTINCT FROM OLD.column_code
                                               OR NEW.column_type IS DISTINCT FROM OLD.column_type
                                               OR NEW.is_nullable IS DISTINCT FROM OLD.is_nullable
                                            THEN
                                                RAISE EXCEPTION 'Reference register field identifiers are immutable after records exist.';
                                            END IF;
                                        END IF;
                                    END IF;

                                    IF TG_OP = 'DELETE' THEN
                                        RETURN OLD;
                                    END IF;

                                    RETURN NEW;
                                END;
                                $$ LANGUAGE plpgsql;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_trigger
                                        WHERE tgname = 'trg_refreg_fields_immutable_when_has_records'
                                          AND tgrelid = 'reference_register_fields'::regclass
                                    ) THEN
                                        CREATE TRIGGER trg_refreg_fields_immutable_when_has_records
                                            BEFORE UPDATE OR DELETE
                                            ON reference_register_fields
                                            FOR EACH ROW
                                            EXECUTE FUNCTION ngb_refreg_forbid_field_mutation_when_has_records();
                                    END IF;
                                END$$;

                                -- Drift repair for fk_refreg_fields__register (CREATE TABLE IF NOT EXISTS will not restore dropped FKs).
                                DO $$
                                BEGIN
                                    IF to_regclass('public.reference_register_fields') IS NULL THEN
                                        RETURN;
                                    END IF;

                                    IF to_regclass('public.reference_registers') IS NULL THEN
                                        RETURN;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint c
                                        JOIN pg_class t ON t.oid = c.conrelid
                                        JOIN pg_namespace n ON n.oid = t.relnamespace
                                        WHERE n.nspname = 'public'
                                          AND t.relname = 'reference_register_fields'
                                          AND c.conname = 'fk_refreg_fields__register'
                                    ) THEN
                                        ALTER TABLE public.reference_register_fields
                                            ADD CONSTRAINT fk_refreg_fields__register
                                                FOREIGN KEY (register_id) REFERENCES public.reference_registers(register_id);
                                    END IF;
                                END
                                $$;
-- <<< ReferenceRegisterFieldsMigration

-- >>> ReferenceRegisterDimensionRulesMigration
                                CREATE TABLE IF NOT EXISTS reference_register_dimension_rules
                                (
                                    register_id    uuid        NOT NULL,
                                    dimension_id   uuid        NOT NULL,
                                    ordinal        integer     NOT NULL,
                                    is_required    boolean     NOT NULL,
                                    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
                                    updated_at_utc timestamptz NOT NULL DEFAULT NOW(),

                                    CONSTRAINT pk_reference_register_dimension_rules
                                        PRIMARY KEY (register_id, dimension_id),

                                    CONSTRAINT ux_reference_register_dimension_rules__register_ordinal
                                        UNIQUE (register_id, ordinal),

                                    CONSTRAINT ck_reference_register_dimension_rules__ordinal_positive
                                        CHECK (ordinal > 0),

                                    CONSTRAINT fk_refreg_dim_rules__register
                                        FOREIGN KEY (register_id) REFERENCES reference_registers(register_id),

                                    CONSTRAINT fk_refreg_dim_rules__dimension
                                        FOREIGN KEY (dimension_id) REFERENCES platform_dimensions(dimension_id)
                                );

                                -- Drift repair for timestamp defaults.
                                DO $$
                                BEGIN
                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'reference_register_dimension_rules'
                                          AND column_name = 'created_at_utc'
                                    ) THEN
                                        ALTER TABLE reference_register_dimension_rules
                                            ALTER COLUMN created_at_utc SET DEFAULT NOW();
                                    END IF;

                                    IF EXISTS (
                                        SELECT 1
                                        FROM information_schema.columns
                                        WHERE table_schema = 'public'
                                          AND table_name = 'reference_register_dimension_rules'
                                          AND column_name = 'updated_at_utc'
                                    ) THEN
                                        ALTER TABLE reference_register_dimension_rules
                                            ALTER COLUMN updated_at_utc SET DEFAULT NOW();
                                    END IF;
                                END$$;

                                -- Drift repair for critical foreign keys (CREATE TABLE IF NOT EXISTS will not restore dropped FKs).
                                DO $$
                                BEGIN
                                    IF to_regclass('public.reference_register_dimension_rules') IS NULL THEN
                                        RETURN;
                                    END IF;

                                    IF to_regclass('public.reference_registers') IS NULL OR to_regclass('public.platform_dimensions') IS NULL THEN
                                        RETURN;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint c
                                        JOIN pg_class t ON t.oid = c.conrelid
                                        JOIN pg_namespace n ON n.oid = t.relnamespace
                                        WHERE n.nspname = 'public'
                                          AND t.relname = 'reference_register_dimension_rules'
                                          AND c.conname = 'fk_refreg_dim_rules__register'
                                    ) THEN
                                        ALTER TABLE public.reference_register_dimension_rules
                                            ADD CONSTRAINT fk_refreg_dim_rules__register
                                                FOREIGN KEY (register_id) REFERENCES public.reference_registers(register_id);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint c
                                        JOIN pg_class t ON t.oid = c.conrelid
                                        JOIN pg_namespace n ON n.oid = t.relnamespace
                                        WHERE n.nspname = 'public'
                                          AND t.relname = 'reference_register_dimension_rules'
                                          AND c.conname = 'fk_refreg_dim_rules__dimension'
                                    ) THEN
                                        ALTER TABLE public.reference_register_dimension_rules
                                            ADD CONSTRAINT fk_refreg_dim_rules__dimension
                                                FOREIGN KEY (dimension_id) REFERENCES public.platform_dimensions(dimension_id);
                                    END IF;
                                END
                                $$;
-- <<< ReferenceRegisterDimensionRulesMigration

-- >>> ReferenceRegisterExtraGuardsMigration
                       -- Guard: prevent dangerous registry mutations after records exist.
                       CREATE OR REPLACE FUNCTION ngb_refreg_forbid_register_mutation_when_has_records()
                       RETURNS trigger AS $$
                       BEGIN
                           IF COALESCE(OLD.has_records, FALSE) THEN
                               IF TG_OP = 'DELETE' THEN
                                   RAISE EXCEPTION 'Reference register metadata is immutable after records exist.';
                               END IF;

                               IF TG_OP = 'UPDATE' THEN
                                   -- code drives generated code_norm/table_code (and thus dynamic per-register table names)
                                   IF NEW.register_id IS DISTINCT FROM OLD.register_id
                                      OR NEW.code IS DISTINCT FROM OLD.code
                                      OR NEW.periodicity IS DISTINCT FROM OLD.periodicity
                                      OR NEW.record_mode IS DISTINCT FROM OLD.record_mode
                                   THEN
                                       RAISE EXCEPTION 'Reference register code/periodicity/record_mode are immutable after records exist.';
                                   END IF;

                                   IF OLD.has_records = TRUE AND NEW.has_records = FALSE THEN
                                       RAISE EXCEPTION 'Reference register has_records can never flip back to FALSE.';
                                   END IF;
                               END IF;
                           END IF;

                           IF TG_OP = 'DELETE' THEN
                               RETURN OLD;
                           END IF;

                           RETURN NEW;
                       END;
                       $$ LANGUAGE plpgsql;
                       

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_trigger
        WHERE tgname = 'trg_refreg_registers_immutable_when_has_records'
          AND tgrelid = 'reference_registers'::regclass
    ) THEN
        CREATE TRIGGER trg_refreg_registers_immutable_when_has_records
            BEFORE UPDATE OR DELETE
            ON reference_registers
            FOR EACH ROW
            EXECUTE FUNCTION ngb_refreg_forbid_register_mutation_when_has_records();
    END IF;
END$$;

                       -- Guard: protect dimension rules from destructive changes after records exist.
                       CREATE OR REPLACE FUNCTION ngb_refreg_forbid_dim_rule_mutation_when_has_records()
                       RETURNS trigger AS $$
                       DECLARE
                           has_rec boolean;
                           reg_id uuid;
                       BEGIN
                           reg_id := COALESCE(NEW.register_id, OLD.register_id);

                           SELECT r.has_records
                             INTO has_rec
                             FROM reference_registers r
                            WHERE r.register_id = reg_id;

                           IF COALESCE(has_rec, FALSE) THEN
                               IF TG_OP = 'DELETE' THEN
                                   RAISE EXCEPTION 'Reference register dimension rules are immutable after records exist.';
                               END IF;

                               IF TG_OP = 'INSERT' THEN
                                   -- Adding an optional dimension rule is allowed for forward-only evolution,
                                   -- but adding a required rule would invalidate historical records.
                                   IF COALESCE(NEW.is_required, FALSE) THEN
                                       RAISE EXCEPTION 'Cannot add a required dimension rule after records exist.';
                                   END IF;
                               END IF;

                               IF TG_OP = 'UPDATE' THEN
                                   RAISE EXCEPTION 'Reference register dimension rules are append-only after records exist.';
                               END IF;
                           END IF;

                           IF TG_OP = 'DELETE' THEN
                               RETURN OLD;
                           END IF;

                           RETURN NEW;
                       END;
                       $$ LANGUAGE plpgsql;
                       

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_trigger
        WHERE tgname = 'trg_refreg_dim_rules_immutable_when_has_records'
          AND tgrelid = 'reference_register_dimension_rules'::regclass
    ) THEN
        CREATE TRIGGER trg_refreg_dim_rules_immutable_when_has_records
            BEFORE INSERT OR UPDATE OR DELETE
            ON reference_register_dimension_rules
            FOR EACH ROW
            EXECUTE FUNCTION ngb_refreg_forbid_dim_rule_mutation_when_has_records();
    END IF;
END$$;
-- <<< ReferenceRegisterExtraGuardsMigration

-- >>> ReferenceRegisterWriteStateMigration
                                DO $$
                                BEGIN
                                    IF to_regclass('public.reference_register_write_state') IS NULL
                                       AND to_regclass('public.reference_register_write_log') IS NOT NULL THEN
                                        ALTER TABLE reference_register_write_log RENAME TO reference_register_write_state;
                                    END IF;
                                END
                                $$;

                                CREATE TABLE IF NOT EXISTS reference_register_write_state (
                                    register_id      uuid         NOT NULL,
                                    document_id      uuid         NOT NULL,
                                    operation        smallint     NOT NULL,

                                    attempt_id       uuid         NULL,
                                    started_at_utc   timestamptz  NOT NULL,
                                    completed_at_utc timestamptz  NULL,

                                    CONSTRAINT pk_reference_register_write_state PRIMARY KEY (register_id, document_id, operation),

                                    CONSTRAINT fk_refreg_write_log_register
                                        FOREIGN KEY (register_id)
                                        REFERENCES reference_registers(register_id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT fk_refreg_write_log_document
                                        FOREIGN KEY (document_id)
                                        REFERENCES documents(id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT ck_refreg_write_log_operation
                                        CHECK (operation IN (1, 2, 3)),

                                    CONSTRAINT ck_refreg_write_log_completed_after_started
                                        CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)
                                );

                                ALTER TABLE reference_register_write_state
                                    ADD COLUMN IF NOT EXISTS attempt_id uuid;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'reference_register_write_state'::regclass
                                          AND contype = 'p'
                                    ) THEN
                                        ALTER TABLE reference_register_write_state
                                            ADD CONSTRAINT pk_reference_register_write_state PRIMARY KEY (register_id, document_id, operation);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'reference_register_write_state'::regclass
                                          AND conname = 'fk_refreg_write_log_register'
                                    ) THEN
                                        ALTER TABLE reference_register_write_state
                                            ADD CONSTRAINT fk_refreg_write_log_register
                                                FOREIGN KEY (register_id)
                                                REFERENCES reference_registers(register_id)
                                                ON DELETE CASCADE;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'reference_register_write_state'::regclass
                                          AND conname = 'fk_refreg_write_log_document'
                                    ) THEN
                                        ALTER TABLE reference_register_write_state
                                            ADD CONSTRAINT fk_refreg_write_log_document
                                                FOREIGN KEY (document_id)
                                                REFERENCES documents(id)
                                                ON DELETE CASCADE;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'reference_register_write_state'::regclass
                                          AND conname = 'ck_refreg_write_log_operation'
                                    ) THEN
                                        ALTER TABLE reference_register_write_state
                                            ADD CONSTRAINT ck_refreg_write_log_operation
                                                CHECK (operation IN (1, 2, 3));
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'reference_register_write_state'::regclass
                                          AND conname = 'ck_refreg_write_log_completed_after_started'
                                    ) THEN
                                        ALTER TABLE reference_register_write_state
                                            ADD CONSTRAINT ck_refreg_write_log_completed_after_started
                                                CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc);
                                    END IF;
                                END
                                $$;
-- <<< ReferenceRegisterWriteStateMigration

-- >>> ReferenceRegisterWriteLogHistoryMigration
                                CREATE TABLE IF NOT EXISTS reference_register_write_log_history (
                                    history_id       uuid         PRIMARY KEY,
                                    attempt_id       uuid         NOT NULL,
                                    register_id      uuid         NOT NULL,
                                    document_id      uuid         NOT NULL,
                                    operation        smallint     NOT NULL,
                                    event_kind       smallint     NOT NULL,
                                    occurred_at_utc  timestamptz  NOT NULL,

                                    CONSTRAINT fk_refreg_write_log_history_register
                                        FOREIGN KEY (register_id)
                                        REFERENCES reference_registers(register_id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT fk_refreg_write_log_history_document
                                        FOREIGN KEY (document_id)
                                        REFERENCES documents(id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT ck_refreg_write_log_history_operation
                                        CHECK (operation IN (1, 2, 3)),

                                    CONSTRAINT ck_refreg_write_log_history_event_kind
                                        CHECK (event_kind IN (1, 2, 3))
                                );

                                CREATE INDEX IF NOT EXISTS ix_refreg_write_log_history_document_operation_occurred
                                    ON reference_register_write_log_history(document_id, operation, occurred_at_utc DESC);

                                CREATE INDEX IF NOT EXISTS ix_refreg_write_log_history_attempt
                                    ON reference_register_write_log_history(attempt_id, occurred_at_utc);

                                DO $$
                                BEGIN
                                    IF to_regclass('public.reference_register_write_log_history') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_refreg_write_log_history_append_only ON public.reference_register_write_log_history;
                                        CREATE TRIGGER trg_refreg_write_log_history_append_only
                                            BEFORE UPDATE OR DELETE ON public.reference_register_write_log_history
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;
                                END
                                $$;
-- <<< ReferenceRegisterWriteLogHistoryMigration

-- >>> ReferenceRegisterIndependentWriteStateMigration
                                DO $$
                                BEGIN
                                    IF to_regclass('public.reference_register_independent_write_state') IS NULL
                                       AND to_regclass('public.reference_register_independent_write_log') IS NOT NULL THEN
                                        ALTER TABLE reference_register_independent_write_log RENAME TO reference_register_independent_write_state;
                                    END IF;
                                END
                                $$;

                                CREATE TABLE IF NOT EXISTS reference_register_independent_write_state (
                                    register_id      uuid        NOT NULL,
                                    command_id       uuid        NOT NULL,
                                    operation        smallint    NOT NULL,

                                    attempt_id       uuid        NULL,
                                    started_at_utc   timestamptz NOT NULL,
                                    completed_at_utc timestamptz NULL,

                                    CONSTRAINT pk_refreg_independent_write_log
                                        PRIMARY KEY (register_id, command_id, operation),

                                    CONSTRAINT fk_refreg_ind_write_log_register
                                        FOREIGN KEY (register_id)
                                        REFERENCES reference_registers(register_id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT ck_refreg_ind_write_log_operation
                                        CHECK (operation IN (1, 2)),

                                    CONSTRAINT ck_refreg_ind_write_log_completed_after_started
                                        CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)
                                );

                                ALTER TABLE reference_register_independent_write_state
                                    ADD COLUMN IF NOT EXISTS attempt_id uuid;
-- <<< ReferenceRegisterIndependentWriteStateMigration

-- >>> ReferenceRegisterIndependentWriteLogHistoryMigration
                                CREATE TABLE IF NOT EXISTS reference_register_independent_write_log_history (
                                    history_id       uuid         PRIMARY KEY,
                                    attempt_id       uuid         NOT NULL,
                                    register_id      uuid         NOT NULL,
                                    command_id       uuid         NOT NULL,
                                    operation        smallint     NOT NULL,
                                    event_kind       smallint     NOT NULL,
                                    occurred_at_utc  timestamptz  NOT NULL,

                                    CONSTRAINT fk_refreg_ind_write_log_history_register
                                        FOREIGN KEY (register_id)
                                        REFERENCES reference_registers(register_id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT ck_refreg_ind_write_log_history_operation
                                        CHECK (operation IN (1, 2)),

                                    CONSTRAINT ck_refreg_ind_write_log_history_event_kind
                                        CHECK (event_kind IN (1, 2, 3))
                                );

                                CREATE INDEX IF NOT EXISTS ix_refreg_ind_write_log_history_command_operation_occurred
                                    ON reference_register_independent_write_log_history(command_id, operation, occurred_at_utc DESC);

                                CREATE INDEX IF NOT EXISTS ix_refreg_ind_write_log_history_attempt
                                    ON reference_register_independent_write_log_history(attempt_id, occurred_at_utc);

                                DO $$
                                BEGIN
                                    IF to_regclass('public.reference_register_independent_write_log_history') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_refreg_ind_write_log_history_append_only ON public.reference_register_independent_write_log_history;
                                        CREATE TRIGGER trg_refreg_ind_write_log_history_append_only
                                            BEFORE UPDATE OR DELETE ON public.reference_register_independent_write_log_history
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;
                                END
                                $$;
-- <<< ReferenceRegisterIndependentWriteLogHistoryMigration

-- >>> ReferenceRegistersIndexesMigration
                                -- registry
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_reference_registers_code_norm
                                    ON reference_registers(code_norm);

                                -- physical per-register tables are derived from table_code
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_reference_registers_table_code
                                    ON reference_registers(table_code);

                                -- fields (metadata -> physical column schema)
                                CREATE INDEX IF NOT EXISTS ix_refreg_fields_register_ordinal
                                    ON reference_register_fields(register_id, ordinal, code_norm);

                                -- key dimension rules
                                CREATE INDEX IF NOT EXISTS ix_refreg_dim_rules_register_ordinal
                                    ON reference_register_dimension_rules(register_id, ordinal);

                                -- idempotency log
                                CREATE INDEX IF NOT EXISTS ix_refreg_write_log_document
                                    ON reference_register_write_state(document_id);
-- <<< ReferenceRegistersIndexesMigration

-- >>> PlatformUsersMigration
                                CREATE TABLE IF NOT EXISTS platform_users (
                                    user_id UUID PRIMARY KEY,

                                    auth_subject TEXT NOT NULL,

                                    email TEXT NULL,
                                    display_name TEXT NULL,

                                    is_active BOOLEAN NOT NULL DEFAULT TRUE,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT ck_platform_users_auth_subject_nonempty CHECK (length(trim(auth_subject)) > 0),
                                    CONSTRAINT ck_platform_users_email_nonempty CHECK (email IS NULL OR length(trim(email)) > 0),
                                    CONSTRAINT ck_platform_users_display_name_nonempty CHECK (display_name IS NULL OR length(trim(display_name)) > 0)
                                );
-- <<< PlatformUsersMigration

-- >>> PlatformUsersIndexesMigration
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_platform_users_auth_subject
                                    ON platform_users(auth_subject);

                                CREATE INDEX IF NOT EXISTS ix_platform_users_email
                                    ON platform_users(email)
                                    WHERE email IS NOT NULL;
-- <<< PlatformUsersIndexesMigration

-- >>> AccountingAccountDimensionRulesMigration
                                CREATE TABLE IF NOT EXISTS accounting_account_dimension_rules
                                (
                                    account_id   UUID NOT NULL,
                                    dimension_id UUID NOT NULL,
                                    ordinal      INT  NOT NULL,
                                    is_required  BOOLEAN NOT NULL,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT pk_acc_dim_rules PRIMARY KEY (account_id, dimension_id),

                                    CONSTRAINT ck_acc_dim_rules_ordinal_positive CHECK (ordinal > 0),

                                    CONSTRAINT fk_acc_dim_rules_account
                                        FOREIGN KEY (account_id)
                                        REFERENCES accounting_accounts(account_id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT fk_acc_dim_rules_dimension
                                        FOREIGN KEY (dimension_id)
                                        REFERENCES platform_dimensions(dimension_id)
                                        ON DELETE RESTRICT
                                );

                                -- Critical DB guard: callers rely on specific, named foreign keys (for predictable SqlState/ConstraintName).
                                -- CREATE TABLE IF NOT EXISTS does not restore FKs after drift. Repair if missing or created under different names.
                                DO $$
                                DECLARE
                                    has_account boolean;
                                    has_dimension boolean;
                                    r record;
                                BEGIN
                                    SELECT EXISTS(
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'accounting_account_dimension_rules'::regclass
                                          AND conname = 'fk_acc_dim_rules_account'
                                    ) INTO has_account;

                                    SELECT EXISTS(
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'accounting_account_dimension_rules'::regclass
                                          AND conname = 'fk_acc_dim_rules_dimension'
                                    ) INTO has_dimension;

                                    IF has_account AND has_dimension THEN
                                        RETURN;
                                    END IF;

                                    FOR r IN
                                        SELECT conname
                                        FROM pg_constraint
                                        WHERE conrelid = 'accounting_account_dimension_rules'::regclass
                                          AND contype = 'f'
                                    LOOP
                                        EXECUTE format('ALTER TABLE accounting_account_dimension_rules DROP CONSTRAINT IF EXISTS %I', r.conname);
                                    END LOOP;

                                    ALTER TABLE accounting_account_dimension_rules
                                        ADD CONSTRAINT fk_acc_dim_rules_account
                                            FOREIGN KEY (account_id)
                                            REFERENCES accounting_accounts(account_id)
                                            ON DELETE CASCADE;

                                    ALTER TABLE accounting_account_dimension_rules
                                        ADD CONSTRAINT fk_acc_dim_rules_dimension
                                            FOREIGN KEY (dimension_id)
                                            REFERENCES platform_dimensions(dimension_id)
                                            ON DELETE RESTRICT;
                                END $$;
-- <<< AccountingAccountDimensionRulesMigration

-- >>> AccountingAccountDimensionRulesIndexesMigration
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_acc_dim_rules_account_ordinal
                                    ON accounting_account_dimension_rules(account_id, ordinal);

                                CREATE INDEX IF NOT EXISTS ix_acc_dim_rules_dimension_id
                                    ON accounting_account_dimension_rules(dimension_id);
-- <<< AccountingAccountDimensionRulesIndexesMigration

-- >>> PlatformAuditEventsMigration
                                CREATE TABLE IF NOT EXISTS platform_audit_events (
                                    audit_event_id UUID PRIMARY KEY,

                                    entity_kind SMALLINT NOT NULL,
                                    entity_id UUID NOT NULL,

                                    action_code TEXT NOT NULL,

                                    actor_user_id UUID NULL,

                                    occurred_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    correlation_id UUID NULL,
                                    metadata JSONB NULL,

                                    CONSTRAINT ck_platform_audit_events_action_code_nonempty CHECK (length(trim(action_code)) > 0),
                                    CONSTRAINT ck_platform_audit_events_action_code_maxlen CHECK (length(action_code) <= 200),

                                    CONSTRAINT fk_platform_audit_events_actor_user
                                        FOREIGN KEY (actor_user_id)
                                            REFERENCES platform_users(user_id)
                                            ON UPDATE RESTRICT
                                            ON DELETE RESTRICT
                                );
-- <<< PlatformAuditEventsMigration

-- >>> PlatformAuditEventChangesMigration
                                CREATE TABLE IF NOT EXISTS platform_audit_event_changes (
                                    audit_change_id UUID PRIMARY KEY,

                                    audit_event_id UUID NOT NULL,
                                    ordinal INTEGER NOT NULL,

                                    field_path TEXT NOT NULL,

                                    old_value_jsonb JSONB NULL,
                                    new_value_jsonb JSONB NULL,

                                    CONSTRAINT fk_platform_audit_event_changes_event
                                        FOREIGN KEY (audit_event_id)
                                            REFERENCES platform_audit_events(audit_event_id)
                                            ON UPDATE RESTRICT
                                            ON DELETE RESTRICT,

                                    CONSTRAINT ck_platform_audit_event_changes_ordinal_positive CHECK (ordinal > 0),
                                    CONSTRAINT ck_platform_audit_event_changes_field_path_nonempty CHECK (length(trim(field_path)) > 0),
                                    CONSTRAINT ck_platform_audit_event_changes_field_path_maxlen CHECK (length(field_path) <= 400)
                                );
-- <<< PlatformAuditEventChangesMigration

-- >>> PlatformAuditAppendOnlyGuardMigration
                                DO $$
                                BEGIN
                                    IF to_regclass('public.platform_audit_events') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_platform_audit_events_append_only ON public.platform_audit_events;
                                        CREATE TRIGGER trg_platform_audit_events_append_only
                                            BEFORE UPDATE OR DELETE ON public.platform_audit_events
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;

                                    IF to_regclass('public.platform_audit_event_changes') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_platform_audit_event_changes_append_only ON public.platform_audit_event_changes;
                                        CREATE TRIGGER trg_platform_audit_event_changes_append_only
                                            BEFORE UPDATE OR DELETE ON public.platform_audit_event_changes
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;
                                END
                                $$;
-- <<< PlatformAuditAppendOnlyGuardMigration

-- >>> PlatformAuditIndexesMigration
                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_occurred_at
                                    ON platform_audit_events(occurred_at_utc);

                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_entity
                                    ON platform_audit_events(entity_kind, entity_id, occurred_at_utc DESC);

                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_action
                                    ON platform_audit_events(action_code, occurred_at_utc DESC);

                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_actor
                                    ON platform_audit_events(actor_user_id, occurred_at_utc DESC)
                                    WHERE actor_user_id IS NOT NULL;

                                CREATE INDEX IF NOT EXISTS ix_platform_audit_event_changes_event
                                    ON platform_audit_event_changes(audit_event_id, ordinal);

                                CREATE UNIQUE INDEX IF NOT EXISTS ux_platform_audit_event_changes_event_ordinal
                                    ON platform_audit_event_changes(audit_event_id, ordinal);
-- <<< PlatformAuditIndexesMigration

-- >>> PlatformAuditPagingIndexesMigration
                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_occurred_at_id_desc
                                    ON platform_audit_events(occurred_at_utc DESC, audit_event_id DESC);

                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_entity_occurred_at_id_desc
                                    ON platform_audit_events(entity_kind, entity_id, occurred_at_utc DESC, audit_event_id DESC);


                                -- Stable paging for "actor" filter (same cursor shape as global paging).
                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_actor_occurred_at_id_desc
                                    ON platform_audit_events(actor_user_id, occurred_at_utc DESC, audit_event_id DESC)
                                    WHERE actor_user_id IS NOT NULL;

                                -- Stable paging for "action_code" filter.
                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_action_occurred_at_id_desc
                                    ON platform_audit_events(action_code, occurred_at_utc DESC, audit_event_id DESC);

                                -- Stable paging for correlation tracing.
                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_correlation_occurred_at_id_desc
                                    ON platform_audit_events(correlation_id, occurred_at_utc DESC, audit_event_id DESC)
                                    WHERE correlation_id IS NOT NULL;
-- <<< PlatformAuditPagingIndexesMigration

-- >>> GeneralJournalEntryMigration
CREATE TABLE IF NOT EXISTS doc_general_journal_entry
(
    document_id uuid PRIMARY KEY REFERENCES documents (id) ON DELETE CASCADE,
    journal_type smallint NOT NULL,
    source smallint NOT NULL,
    approval_state smallint NOT NULL,
    reason_code text NULL,
    memo text NULL,
    external_reference text NULL,
    auto_reverse boolean NOT NULL DEFAULT FALSE,
    auto_reverse_on_utc date NULL,
    reversal_of_document_id uuid NULL REFERENCES documents (id),
    initiated_by text NULL,
    initiated_at_utc timestamptz NULL,
    submitted_by text NULL,
    submitted_at_utc timestamptz NULL,
    approved_by text NULL,
    approved_at_utc timestamptz NULL,
    rejected_by text NULL,
    rejected_at_utc timestamptz NULL,
    reject_reason text NULL,
    posted_by text NULL,
    posted_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL DEFAULT NOW(),
    updated_at_utc timestamptz NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_doc_gje_journal_type CHECK (journal_type IN (1, 2, 3, 4, 5)),
    CONSTRAINT ck_doc_gje_source CHECK (source IN (1, 2)),
    CONSTRAINT ck_doc_gje_approval_state CHECK (approval_state IN (1, 2, 3, 4)),

    -- Once submitted (or beyond), a GJE must have business meaning in a stable form.
    CONSTRAINT ck_doc_gje_reason_memo_required CHECK (
        approval_state = 1 OR (reason_code IS NOT NULL AND memo IS NOT NULL)
    ),

    -- Auto reverse is only a setting on non-reversal entries.
    CONSTRAINT ck_doc_gje_auto_reverse_fields CHECK (
        (auto_reverse = FALSE AND auto_reverse_on_utc IS NULL)
        OR
        (auto_reverse = TRUE AND auto_reverse_on_utc IS NOT NULL AND reversal_of_document_id IS NULL)
    ),

    -- Reversal entries reference the original.
    CONSTRAINT ck_doc_gje_reversal_doc CHECK (
        reversal_of_document_id IS NULL
        OR (journal_type = 2 AND source = 2 AND auto_reverse = FALSE AND auto_reverse_on_utc IS NULL)
    ),

    -- System documents are always pre-approved (e.g. scheduled reversals).
    CONSTRAINT ck_doc_gje_system_is_approved CHECK (source <> 2 OR approval_state = 3),

    -- Approval state gates audit columns.
    CONSTRAINT ck_doc_gje_submission_state CHECK (
        (approval_state = 1 AND submitted_at_utc IS NULL AND submitted_by IS NULL AND approved_at_utc IS NULL AND approved_by IS NULL AND rejected_at_utc IS NULL AND rejected_by IS NULL AND reject_reason IS NULL)
        OR
        (approval_state = 2 AND submitted_at_utc IS NOT NULL AND submitted_by IS NOT NULL AND approved_at_utc IS NULL AND approved_by IS NULL AND rejected_at_utc IS NULL AND rejected_by IS NULL AND reject_reason IS NULL)
        OR
        (approval_state = 3 AND approved_at_utc IS NOT NULL AND approved_by IS NOT NULL AND rejected_at_utc IS NULL AND rejected_by IS NULL AND reject_reason IS NULL)
        OR
        (approval_state = 4 AND rejected_at_utc IS NOT NULL AND rejected_by IS NOT NULL AND reject_reason IS NOT NULL AND approved_at_utc IS NULL AND approved_by IS NULL)
    )
);

CREATE TABLE IF NOT EXISTS doc_general_journal_entry__lines
(
    document_id uuid NOT NULL,
    line_no int NOT NULL,
    side smallint NOT NULL,
    -- IMPORTANT: accounting_accounts PK is account_id, not id.
    account_id uuid NOT NULL REFERENCES accounting_accounts (account_id),
    amount numeric(19,4) NOT NULL,
    memo text NULL,
    dimension_set_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',


    -- Keep the header FK (defense in depth: lines cannot exist without header).
    CONSTRAINT fk_doc_gje_lines__header
        FOREIGN KEY (document_id) REFERENCES doc_general_journal_entry (document_id) ON DELETE CASCADE,

    -- Platform invariant: every typed table must be linked to documents(id).
    CONSTRAINT fk_doc_gje_lines__document
        FOREIGN KEY (document_id) REFERENCES documents (id),

    CONSTRAINT fk_doc_gje_lines__dimension_set
        FOREIGN KEY (dimension_set_id) REFERENCES platform_dimension_sets (dimension_set_id),

    PRIMARY KEY (document_id, line_no),
    CONSTRAINT ck_doc_gje_lines_side CHECK (side IN (1, 2)),
    CONSTRAINT ck_doc_gje_lines_amount CHECK (amount > 0)
);

CREATE TABLE IF NOT EXISTS doc_general_journal_entry__allocations
(
    document_id uuid NOT NULL,
    entry_no int NOT NULL,
    debit_line_no int NOT NULL,
    credit_line_no int NOT NULL,
    amount numeric(19,4) NOT NULL,

    -- Header FK keeps allocations tied to the GJE document.
    CONSTRAINT fk_doc_gje_alloc__header
        FOREIGN KEY (document_id) REFERENCES doc_general_journal_entry (document_id) ON DELETE CASCADE,

    -- Platform invariant: every typed table must be linked to documents(id).
    CONSTRAINT fk_doc_gje_alloc__document
        FOREIGN KEY (document_id) REFERENCES documents (id),

    PRIMARY KEY (document_id, entry_no),
    CONSTRAINT ck_doc_gje_alloc_amount CHECK (amount > 0),
    CONSTRAINT fk_doc_gje_alloc_debit FOREIGN KEY (document_id, debit_line_no) REFERENCES doc_general_journal_entry__lines (document_id, line_no) ON DELETE CASCADE,
    CONSTRAINT fk_doc_gje_alloc_credit FOREIGN KEY (document_id, credit_line_no) REFERENCES doc_general_journal_entry__lines (document_id, line_no) ON DELETE CASCADE
);

-- ----------------------------------------------------------
-- Drift repair (idempotent): ensure required FKs exist even
-- if tables were created by an older migration version.
-- ----------------------------------------------------------
DO $$
BEGIN
    -- Lines: ensure FK document_id -> documents(id) exists
    IF to_regclass('public.doc_general_journal_entry__lines') IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name
             AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema = tc.table_schema
            WHERE tc.table_schema = 'public'
              AND tc.table_name = 'doc_general_journal_entry__lines'
              AND tc.constraint_type = 'FOREIGN KEY'
              AND kcu.column_name = 'document_id'
              AND ccu.table_name = 'documents'
              AND ccu.column_name = 'id'
        ) THEN
            ALTER TABLE doc_general_journal_entry__lines
                ADD CONSTRAINT fk_doc_gje_lines__document
                    FOREIGN KEY (document_id) REFERENCES documents (id);
        END IF;


        -- Lines: ensure dimension_set_id column + FK exist
        IF NOT EXISTS (
            SELECT 1
            FROM information_schema.columns c
            WHERE c.table_schema = 'public'
              AND c.table_name = 'doc_general_journal_entry__lines'
              AND c.column_name = 'dimension_set_id'
        ) THEN
            ALTER TABLE doc_general_journal_entry__lines
                ADD COLUMN dimension_set_id uuid;

            UPDATE doc_general_journal_entry__lines
                SET dimension_set_id = '00000000-0000-0000-0000-000000000000'
            WHERE dimension_set_id IS NULL;

            ALTER TABLE doc_general_journal_entry__lines
                ALTER COLUMN dimension_set_id SET DEFAULT '00000000-0000-0000-0000-000000000000',
                ALTER COLUMN dimension_set_id SET NOT NULL;
        END IF;

        IF NOT EXISTS (
            SELECT 1
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name
             AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema = tc.table_schema
            WHERE tc.table_schema = 'public'
              AND tc.table_name = 'doc_general_journal_entry__lines'
              AND tc.constraint_type = 'FOREIGN KEY'
              AND kcu.column_name = 'dimension_set_id'
              AND ccu.table_name = 'platform_dimension_sets'
              AND ccu.column_name = 'dimension_set_id'
        ) THEN
            ALTER TABLE doc_general_journal_entry__lines
                ADD CONSTRAINT fk_doc_gje_lines__dimension_set
                    FOREIGN KEY (dimension_set_id) REFERENCES platform_dimension_sets (dimension_set_id);
        END IF;
    END IF;

    -- Allocations: ensure FK document_id -> documents(id) exists
    IF to_regclass('public.doc_general_journal_entry__allocations') IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name
             AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema = tc.table_schema
            WHERE tc.table_schema = 'public'
              AND tc.table_name = 'doc_general_journal_entry__allocations'
              AND tc.constraint_type = 'FOREIGN KEY'
              AND kcu.column_name = 'document_id'
              AND ccu.table_name = 'documents'
              AND ccu.column_name = 'id'
        ) THEN
            ALTER TABLE doc_general_journal_entry__allocations
                ADD CONSTRAINT fk_doc_gje_alloc__document
                    FOREIGN KEY (document_id) REFERENCES documents (id);
        END IF;
    END IF;
END $$;

-- ==========================================================
-- Posted document immutability guard (defense in depth)
-- ==========================================================
-- NOTE: These are platform invariants. We attach the immutability triggers
-- at the migration that creates the typed storages to avoid bootstrapping
-- drift (schema validation expects these guards to exist).

CREATE OR REPLACE FUNCTION ngb_forbid_mutation_of_posted_document()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    st smallint;
    doc_id uuid;
BEGIN
    doc_id := COALESCE(NEW.document_id, OLD.document_id);

    SELECT status INTO st
    FROM documents
    WHERE id = doc_id;

    -- DocumentStatus.Posted is expected to be 2 in the platform enum.
    IF COALESCE(st, 0) = 2 THEN
        RAISE EXCEPTION 'Document is posted and immutable: %', doc_id
            USING ERRCODE = '55000';
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_doc_gje_posted_immutable') THEN
        CREATE TRIGGER trg_doc_gje_posted_immutable
            BEFORE INSERT OR UPDATE OR DELETE ON doc_general_journal_entry
            FOR EACH ROW
            EXECUTE FUNCTION ngb_forbid_mutation_of_posted_document();
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_doc_gje_lines_posted_immutable') THEN
        CREATE TRIGGER trg_doc_gje_lines_posted_immutable
            BEFORE INSERT OR UPDATE OR DELETE ON doc_general_journal_entry__lines
            FOR EACH ROW
            EXECUTE FUNCTION ngb_forbid_mutation_of_posted_document();
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_doc_gje_alloc_posted_immutable') THEN
        CREATE TRIGGER trg_doc_gje_alloc_posted_immutable
            BEFORE INSERT OR UPDATE OR DELETE ON doc_general_journal_entry__allocations
            FOR EACH ROW
            EXECUTE FUNCTION ngb_forbid_mutation_of_posted_document();
    END IF;
END
$$;
-- <<< GeneralJournalEntryMigration

-- >>> ManualGeneralJournalEntryImmutabilityAfterSubmitGuardMigration
CREATE OR REPLACE FUNCTION ngb_forbid_mutation_of_manual_gje_business_fields_when_not_draft()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    doc_status smallint;
BEGIN
    -- If the document is already Posted, the generic posted immutability guard is the single source of truth.
    SELECT status INTO doc_status FROM documents WHERE id = COALESCE(NEW.document_id, OLD.document_id);
    IF COALESCE(doc_status, 0) = 2 THEN
        IF TG_OP = 'DELETE' THEN
            RETURN OLD;
        END IF;

        RETURN NEW;
    END IF;


    IF TG_OP <> 'UPDATE' THEN
        IF TG_OP = 'DELETE' THEN
            RETURN OLD;
        END IF;

        RETURN NEW;
    END IF;

    -- Only manual entries are governed by this guard.
    IF COALESCE(OLD.source, 0) <> 1 THEN
        RETURN NEW;
    END IF;

    -- Draft is editable.
    IF COALESCE(OLD.approval_state, 0) = 1 THEN
        RETURN NEW;
    END IF;

    -- Business fields must not change once submitted/approved/rejected.
    IF (NEW.journal_type IS DISTINCT FROM OLD.journal_type)
        OR (NEW.source IS DISTINCT FROM OLD.source)
        OR (NEW.reason_code IS DISTINCT FROM OLD.reason_code)
        OR (NEW.memo IS DISTINCT FROM OLD.memo)
        OR (NEW.external_reference IS DISTINCT FROM OLD.external_reference)
        OR (NEW.auto_reverse IS DISTINCT FROM OLD.auto_reverse)
        OR (NEW.auto_reverse_on_utc IS DISTINCT FROM OLD.auto_reverse_on_utc)
        OR (NEW.reversal_of_document_id IS DISTINCT FROM OLD.reversal_of_document_id)
        OR (NEW.initiated_by IS DISTINCT FROM OLD.initiated_by)
        OR (NEW.initiated_at_utc IS DISTINCT FROM OLD.initiated_at_utc)
    THEN
        RAISE EXCEPTION 'Manual GJE is immutable after submission: %', COALESCE(NEW.document_id, OLD.document_id)
            USING ERRCODE = '55000';
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION ngb_forbid_mutation_of_manual_gje_lines_when_not_draft()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    st smallint;
    src smallint;
    doc_status smallint;
    doc_id uuid;
BEGIN
    doc_id := COALESCE(NEW.document_id, OLD.document_id);

    SELECT d.status, g.approval_state, g.source
      INTO doc_status, st, src
      FROM documents d
      JOIN doc_general_journal_entry g ON g.document_id = d.id
     WHERE d.id = doc_id;

    IF COALESCE(doc_status, 0) = 2 THEN
        IF TG_OP = 'DELETE' THEN
            RETURN OLD;
        END IF;

        RETURN NEW;
    END IF;

    IF COALESCE(src, 0) = 1 AND COALESCE(st, 0) <> 1 THEN
        RAISE EXCEPTION 'Manual GJE lines are immutable after submission: %', doc_id
            USING ERRCODE = '55000';
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION ngb_forbid_mutation_of_manual_gje_allocations_when_submitted_or_rejected()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    st smallint;
    src smallint;
    doc_status smallint;
    doc_id uuid;
BEGIN
    doc_id := COALESCE(NEW.document_id, OLD.document_id);

    SELECT d.status, g.approval_state, g.source
      INTO doc_status, st, src
      FROM documents d
      JOIN doc_general_journal_entry g ON g.document_id = d.id
     WHERE d.id = doc_id;

    IF COALESCE(doc_status, 0) = 2 THEN
        IF TG_OP = 'DELETE' THEN
            RETURN OLD;
        END IF;

        RETURN NEW;
    END IF;

    -- Allocations are expected to be written during posting (Approved state).
    -- They must never be mutated for manual documents in Submitted/Rejected states.
    IF COALESCE(src, 0) = 1 AND COALESCE(st, 0) IN (2, 4) THEN
        RAISE EXCEPTION 'Manual GJE allocations are not allowed in state % for document %', st, doc_id
            USING ERRCODE = '55000';
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_doc_gje_manual_header_immutable_after_submit') THEN
        CREATE TRIGGER trg_doc_gje_manual_header_immutable_after_submit
            BEFORE UPDATE ON doc_general_journal_entry
            FOR EACH ROW
            EXECUTE FUNCTION ngb_forbid_mutation_of_manual_gje_business_fields_when_not_draft();
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_doc_gje_manual_lines_immutable_after_submit') THEN
        CREATE TRIGGER trg_doc_gje_manual_lines_immutable_after_submit
            BEFORE INSERT OR UPDATE OR DELETE ON doc_general_journal_entry__lines
            FOR EACH ROW
            EXECUTE FUNCTION ngb_forbid_mutation_of_manual_gje_lines_when_not_draft();
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_doc_gje_manual_alloc_state_guard') THEN
        CREATE TRIGGER trg_doc_gje_manual_alloc_state_guard
            BEFORE INSERT OR UPDATE OR DELETE ON doc_general_journal_entry__allocations
            FOR EACH ROW
            EXECUTE FUNCTION ngb_forbid_mutation_of_manual_gje_allocations_when_submitted_or_rejected();
    END IF;
END
$$;
-- <<< ManualGeneralJournalEntryImmutabilityAfterSubmitGuardMigration

-- >>> GeneralJournalEntryIndexesMigration
-- Only one system reversal per original document.
CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_gje_reversal_of_document_id
    ON doc_general_journal_entry (reversal_of_document_id)
    WHERE reversal_of_document_id IS NOT NULL;

-- Fast lookup for runner: due system reversals.
CREATE INDEX IF NOT EXISTS ix_doc_gje_system_reversals
    ON doc_general_journal_entry (source, journal_type, approval_state, posted_at_utc);

CREATE INDEX IF NOT EXISTS ix_doc_gje_lines_document_side
    ON doc_general_journal_entry__lines (document_id, side, line_no);

CREATE INDEX IF NOT EXISTS ix_doc_gje_alloc_debit_line
    ON doc_general_journal_entry__allocations (document_id, debit_line_no);

CREATE INDEX IF NOT EXISTS ix_doc_gje_alloc_credit_line
    ON doc_general_journal_entry__allocations (document_id, credit_line_no);
-- <<< GeneralJournalEntryIndexesMigration

-- >>> PostedDocumentImmutabilityGuardMigration
                                CREATE OR REPLACE FUNCTION ngb_forbid_mutation_of_posted_document()
                                RETURNS trigger AS $$
                                DECLARE
                                    doc_id uuid;
                                    st smallint;
                                BEGIN
                                    -- For INSERT/UPDATE we have NEW; for DELETE only OLD exists.
                                    IF TG_OP = 'DELETE' THEN
                                        doc_id := OLD.document_id;
                                    ELSE
                                        doc_id := NEW.document_id;
                                    END IF;

                                    SELECT d.status INTO st
                                    FROM documents d
                                    WHERE d.id = doc_id;

                                    IF COALESCE(st, 0) = 2 THEN
                                        RAISE EXCEPTION 'Document is posted and immutable: %', doc_id
                                            USING ERRCODE = '55000';
                                    END IF;

                                    IF TG_OP = 'DELETE' THEN
                                        RETURN OLD;
                                    END IF;

                                    RETURN NEW;
                                END;
                                $$ LANGUAGE plpgsql;

                                CREATE OR REPLACE FUNCTION ngb_install_typed_document_immutability_guards()
                                RETURNS void AS $$
                                DECLARE
                                    r record;
                                BEGIN
                                    FOR r IN
                                        SELECT DISTINCT c.table_schema, c.table_name
                                        FROM information_schema.columns c
                                        WHERE c.table_schema = 'public'
                                          AND c.column_name = 'document_id'
                                          AND c.table_name LIKE 'doc\_%' ESCAPE '\'
                                        ORDER BY c.table_name
                                    LOOP
                                        -- Table may have been dropped between snapshot and installation.
                                        IF to_regclass(format('%I.%I', r.table_schema, r.table_name)) IS NULL THEN
                                            CONTINUE;
                                        END IF;

                                        IF NOT EXISTS (
                                            SELECT 1
                                            FROM pg_trigger t
                                            JOIN pg_class cl ON cl.oid = t.tgrelid
                                            JOIN pg_namespace ns ON ns.oid = cl.relnamespace
                                            WHERE t.tgname = 'trg_posted_immutable'
                                              AND ns.nspname = r.table_schema
                                              AND cl.relname = r.table_name
                                        ) THEN
                                            EXECUTE format(
                                                'CREATE TRIGGER trg_posted_immutable BEFORE INSERT OR UPDATE OR DELETE ON %I.%I FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_posted_document();',
                                                r.table_schema,
                                                r.table_name
                                            );
                                        END IF;
                                    END LOOP;
                                END;
                                $$ LANGUAGE plpgsql;

                                -- Cleanup: drop old GJE-specific trigger names (if any),
                                -- then ensure the reusable trigger exists on all typed tables.
                                DO $$
                                BEGIN
                                    IF to_regclass('public.doc_general_journal_entry') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_doc_gje_posted_immutable ON public.doc_general_journal_entry;
                                    END IF;

                                    IF to_regclass('public.doc_general_journal_entry__lines') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_doc_gje_lines_posted_immutable ON public.doc_general_journal_entry__lines;
                                    END IF;

                                    IF to_regclass('public.doc_general_journal_entry__allocations') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_doc_gje_alloc_posted_immutable ON public.doc_general_journal_entry__allocations;
                                    END IF;
                                END $$;

                                SELECT ngb_install_typed_document_immutability_guards();
-- <<< PostedDocumentImmutabilityGuardMigration

-- >>> PostedDocumentHeaderImmutabilityGuardMigration
                                CREATE OR REPLACE FUNCTION ngb_forbid_mutation_of_posted_document_header()
                                RETURNS trigger AS $$
                                BEGIN
                                    -- 2 = DocumentStatus.Posted
                                    IF COALESCE(OLD.status, 0) = 2 THEN
                                        IF TG_OP = 'DELETE' THEN
                                            RAISE EXCEPTION 'Document is posted and immutable: %', OLD.id
                                                USING ERRCODE = '55000';
                                        END IF;

                                        -- UPDATE: allow only lifecycle fields while the document remains posted.
                                        -- If status changes away from Posted (unpost), we allow it as well,
                                        -- but still forbid changing any non-lifecycle fields in the same statement.
                                        IF (NEW.id <> OLD.id)
                                           OR (NEW.type_code IS DISTINCT FROM OLD.type_code)
                                           OR (NEW.date_utc IS DISTINCT FROM OLD.date_utc)
                                           OR (NEW.number IS DISTINCT FROM OLD.number)
                                           OR (NEW.created_at_utc IS DISTINCT FROM OLD.created_at_utc)
                                        THEN
                                            RAISE EXCEPTION 'Document is posted and immutable: %', OLD.id
                                                USING ERRCODE = '55000';
                                        END IF;
                                    END IF;

                                    IF TG_OP = 'DELETE' THEN
                                        RETURN OLD;
                                    END IF;

                                    RETURN NEW;
                                END;
                                $$ LANGUAGE plpgsql;

                                -- (Re)install trigger deterministically.
                                DROP TRIGGER IF EXISTS trg_documents_posted_immutable ON public.documents;
                                CREATE TRIGGER trg_documents_posted_immutable
                                BEFORE UPDATE OR DELETE ON public.documents
                                FOR EACH ROW
                                EXECUTE FUNCTION ngb_forbid_mutation_of_posted_document_header();
-- <<< PostedDocumentHeaderImmutabilityGuardMigration

-- -----------------------------------------------------------------------------
-- Final clean-baseline additions that supersede the old incremental platform
-- migrations.
-- -----------------------------------------------------------------------------

-- >>> DocumentOperationStateHistoryMigration
CREATE TABLE IF NOT EXISTS platform_document_operation_state (
    document_id      uuid         NOT NULL,
    operation        smallint     NOT NULL,
    attempt_id       uuid         NOT NULL,
    started_at_utc   timestamptz  NOT NULL,
    completed_at_utc timestamptz  NULL,

    CONSTRAINT pk_platform_document_operation_state
        PRIMARY KEY (document_id, operation),

    CONSTRAINT fk_platform_document_operation_state_document
        FOREIGN KEY (document_id) REFERENCES documents(id),

    CONSTRAINT ck_platform_document_operation_state_operation
        CHECK (operation IN (1, 2, 3, 4)),

    CONSTRAINT ck_platform_document_operation_state_completed_after_started
        CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)
);

CREATE TABLE IF NOT EXISTS platform_document_operation_history (
    history_id       uuid         PRIMARY KEY,
    attempt_id       uuid         NOT NULL,
    document_id      uuid         NOT NULL,
    operation        smallint     NOT NULL,
    event_kind       smallint     NOT NULL,
    occurred_at_utc  timestamptz  NOT NULL,

    CONSTRAINT fk_platform_document_operation_history_document
        FOREIGN KEY (document_id) REFERENCES documents(id),

    CONSTRAINT ck_platform_document_operation_history_operation
        CHECK (operation IN (1, 2, 3, 4)),

    CONSTRAINT ck_platform_document_operation_history_event_kind
        CHECK (event_kind IN (1, 2, 3))
);

CREATE INDEX IF NOT EXISTS ix_platform_document_operation_state_started
    ON platform_document_operation_state(started_at_utc);

CREATE INDEX IF NOT EXISTS ix_platform_document_operation_state_completed
    ON platform_document_operation_state(completed_at_utc);

CREATE INDEX IF NOT EXISTS ix_platform_document_operation_history_document_occurred
    ON platform_document_operation_history(document_id, occurred_at_utc DESC, history_id DESC);

CREATE INDEX IF NOT EXISTS ix_platform_document_operation_history_attempt
    ON platform_document_operation_history(attempt_id, occurred_at_utc DESC, history_id DESC);

DO $$
BEGIN
    IF to_regclass('public.platform_document_operation_history') IS NOT NULL THEN
        DROP TRIGGER IF EXISTS trg_platform_document_operation_history_append_only ON public.platform_document_operation_history;
        CREATE TRIGGER trg_platform_document_operation_history_append_only
            BEFORE UPDATE OR DELETE ON public.platform_document_operation_history
            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
    END IF;
END
$$;
-- <<< DocumentOperationStateHistoryMigration

-- >>> AccountingIndexesAndCashFlowMetadataMigration
CREATE INDEX IF NOT EXISTS ix_accounting_posting_state_page_order
    ON accounting_posting_state(started_at_utc DESC, document_id DESC, operation DESC);

CREATE INDEX IF NOT EXISTS ix_accounting_posting_state_operation_page_order
    ON accounting_posting_state(operation, started_at_utc DESC, document_id DESC);

CREATE INDEX IF NOT EXISTS ix_accounting_posting_state_incomplete_page_order
    ON accounting_posting_state(started_at_utc DESC, document_id DESC, operation DESC)
    WHERE completed_at_utc IS NULL;

CREATE INDEX IF NOT EXISTS ix_acc_reg_general_journal_page_order
    ON accounting_register_main(period, entry_id);

CREATE INDEX IF NOT EXISTS ix_acc_reg_general_journal_document_page_order
    ON accounting_register_main(document_id, period, entry_id);

CREATE INDEX IF NOT EXISTS ix_acc_reg_general_journal_debit_page_order
    ON accounting_register_main(debit_account_id, period, entry_id);

CREATE INDEX IF NOT EXISTS ix_acc_reg_general_journal_credit_page_order
    ON accounting_register_main(credit_account_id, period, entry_id);

CREATE INDEX IF NOT EXISTS ix_acc_reg_account_card_debit_dim_page_order
    ON accounting_register_main(debit_account_id, debit_dimension_set_id, period, entry_id);

CREATE INDEX IF NOT EXISTS ix_acc_reg_account_card_credit_dim_page_order
    ON accounting_register_main(credit_account_id, credit_dimension_set_id, period, entry_id);

CREATE INDEX IF NOT EXISTS ix_acc_reg_gl_agg_debit_group_page_order
    ON accounting_register_main(debit_account_id, period, document_id, credit_account_id, debit_dimension_set_id);

CREATE INDEX IF NOT EXISTS ix_acc_reg_gl_agg_credit_group_page_order
    ON accounting_register_main(credit_account_id, period, document_id, debit_account_id, credit_dimension_set_id);

CREATE TABLE IF NOT EXISTS accounting_cash_flow_lines (
    line_code TEXT PRIMARY KEY,
    method SMALLINT NOT NULL,
    section SMALLINT NOT NULL,
    label TEXT NOT NULL,
    sort_order INT NOT NULL,
    is_system BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_acc_cash_flow_lines_code_nonempty CHECK (length(trim(line_code)) > 0),
    CONSTRAINT ck_acc_cash_flow_lines_label_nonempty CHECK (length(trim(label)) > 0),
    CONSTRAINT ck_acc_cash_flow_lines_method_range CHECK (method BETWEEN 1 AND 1),
    CONSTRAINT ck_acc_cash_flow_lines_section_range CHECK (section BETWEEN 1 AND 4)
);

INSERT INTO accounting_cash_flow_lines(line_code, method, section, label, sort_order, is_system, created_at_utc, updated_at_utc)
VALUES
    ('op_wc_accounts_receivable', 1, 1, 'Change in Accounts Receivable', 110, TRUE, NOW(), NOW()),
    ('op_wc_accounts_payable', 1, 1, 'Change in Accounts Payable', 120, TRUE, NOW(), NOW()),
    ('op_wc_inventory', 1, 1, 'Change in Inventory', 130, TRUE, NOW(), NOW()),
    ('op_wc_prepaids', 1, 1, 'Change in Prepaid Expenses', 140, TRUE, NOW(), NOW()),
    ('op_wc_other_current_assets', 1, 1, 'Change in Other Current Assets', 150, TRUE, NOW(), NOW()),
    ('op_wc_accrued_liabilities', 1, 1, 'Change in Accrued Liabilities', 160, TRUE, NOW(), NOW()),
    ('op_wc_other_current_liabilities', 1, 1, 'Change in Other Current Liabilities', 170, TRUE, NOW(), NOW()),
    ('op_adjust_depreciation_amortization', 1, 1, 'Depreciation and amortization', 210, TRUE, NOW(), NOW()),
    ('op_adjust_noncash_gains_losses', 1, 1, 'Non-cash gains and losses', 220, TRUE, NOW(), NOW()),
    ('op_adjust_other_noncash', 1, 1, 'Other non-cash operating adjustments', 230, TRUE, NOW(), NOW()),
    ('inv_property_equipment_net', 1, 2, 'Property and equipment, net', 310, TRUE, NOW(), NOW()),
    ('inv_intangibles_net', 1, 2, 'Intangible assets, net', 320, TRUE, NOW(), NOW()),
    ('inv_investments_net', 1, 2, 'Investments, net', 330, TRUE, NOW(), NOW()),
    ('inv_loans_receivable_net', 1, 2, 'Loans receivable, net', 340, TRUE, NOW(), NOW()),
    ('inv_other_net', 1, 2, 'Other investing activities, net', 390, TRUE, NOW(), NOW()),
    ('fin_owner_equity_net', 1, 3, 'Owner equity transactions, net', 410, TRUE, NOW(), NOW()),
    ('fin_distributions_net', 1, 3, 'Owner distributions, net', 420, TRUE, NOW(), NOW()),
    ('fin_debt_net', 1, 3, 'Borrowings and repayments, net', 430, TRUE, NOW(), NOW()),
    ('fin_other_net', 1, 3, 'Other financing activities, net', 490, TRUE, NOW(), NOW())
ON CONFLICT (line_code) DO UPDATE
SET
    method = EXCLUDED.method,
    section = EXCLUDED.section,
    label = EXCLUDED.label,
    sort_order = EXCLUDED.sort_order,
    is_system = EXCLUDED.is_system,
    updated_at_utc = NOW();

ALTER TABLE accounting_accounts
    ADD COLUMN IF NOT EXISTS cash_flow_role SMALLINT NOT NULL DEFAULT 0;

ALTER TABLE accounting_accounts
    ADD COLUMN IF NOT EXISTS cash_flow_line_code TEXT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_acc_accounts_cash_flow_role_range'
    ) THEN
        ALTER TABLE accounting_accounts
            ADD CONSTRAINT ck_acc_accounts_cash_flow_role_range
            CHECK (cash_flow_role BETWEEN 0 AND 5);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_acc_accounts_cash_flow_line_code_trimmed'
    ) THEN
        ALTER TABLE accounting_accounts
            ADD CONSTRAINT ck_acc_accounts_cash_flow_line_code_trimmed
            CHECK (cash_flow_line_code IS NULL OR cash_flow_line_code = btrim(cash_flow_line_code));
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_acc_accounts_cash_flow_line_code_nonempty'
    ) THEN
        ALTER TABLE accounting_accounts
            ADD CONSTRAINT ck_acc_accounts_cash_flow_line_code_nonempty
            CHECK (cash_flow_line_code IS NULL OR length(cash_flow_line_code) > 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_acc_accounts_cash_flow_line_code'
    ) THEN
        ALTER TABLE accounting_accounts
            ADD CONSTRAINT fk_acc_accounts_cash_flow_line_code
            FOREIGN KEY (cash_flow_line_code)
            REFERENCES accounting_cash_flow_lines(line_code);
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS ix_acc_accounts_cash_flow_role
    ON accounting_accounts(cash_flow_role)
    WHERE is_deleted = FALSE AND cash_flow_role <> 0;

CREATE INDEX IF NOT EXISTS ix_acc_accounts_cash_flow_line_code
    ON accounting_accounts(cash_flow_line_code)
    WHERE is_deleted = FALSE AND cash_flow_line_code IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_acc_reg_cash_flow_debit_cash_period_counter
    ON accounting_register_main(debit_account_id, period, credit_account_id);

CREATE INDEX IF NOT EXISTS ix_acc_reg_cash_flow_credit_cash_period_counter
    ON accounting_register_main(credit_account_id, period, debit_account_id);
-- <<< AccountingIndexesAndCashFlowMetadataMigration

-- >>> DocumentRelationshipMirroringMigration
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE OR REPLACE FUNCTION ngb_compute_document_relationship_id(
    p_from_document_id uuid,
    p_relationship_code_norm text,
    p_to_document_id uuid
)
RETURNS uuid AS $$
DECLARE
    stable_input text;
    hash_bytes bytea;
    guid_bytes bytea;
    guid_hex text;
BEGIN
    IF p_from_document_id IS NULL THEN
        RAISE EXCEPTION 'from_document_id is required'
            USING ERRCODE = '22023';
    END IF;

    IF p_to_document_id IS NULL THEN
        RAISE EXCEPTION 'to_document_id is required'
            USING ERRCODE = '22023';
    END IF;

    IF p_relationship_code_norm IS NULL OR btrim(p_relationship_code_norm) = '' THEN
        RAISE EXCEPTION 'relationship_code_norm is required'
            USING ERRCODE = '22023';
    END IF;

    stable_input := format(
        'DocumentRelationship|%s|%s|%s',
        p_from_document_id::text,
        lower(btrim(p_relationship_code_norm)),
        p_to_document_id::text);

    hash_bytes := digest(convert_to(stable_input, 'UTF8'), 'sha256');
    guid_bytes := substring(hash_bytes FROM 1 FOR 16);

    guid_bytes := set_byte(guid_bytes, 7, ((get_byte(guid_bytes, 7) & 15) | 80));
    guid_bytes := set_byte(guid_bytes, 8, ((get_byte(guid_bytes, 8) & 63) | 128));

    guid_hex := encode(guid_bytes, 'hex');

    RETURN (
        substr(guid_hex, 7, 2) || substr(guid_hex, 5, 2) || substr(guid_hex, 3, 2) || substr(guid_hex, 1, 2) || '-' ||
        substr(guid_hex, 11, 2) || substr(guid_hex, 9, 2) || '-' ||
        substr(guid_hex, 15, 2) || substr(guid_hex, 13, 2) || '-' ||
        substr(guid_hex, 17, 4) || '-' ||
        substr(guid_hex, 21, 12)
    )::uuid;
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION ngb_sync_mirrored_document_relationship()
RETURNS trigger AS $$
DECLARE
    v_column_name text := btrim(TG_ARGV[0]);
    v_relationship_code text := btrim(TG_ARGV[1]);
    v_relationship_code_norm text := lower(v_relationship_code);

    v_old_from_document_id uuid;
    v_new_from_document_id uuid;
    v_old_to_document_id uuid;
    v_new_to_document_id uuid;
BEGIN
    IF v_column_name IS NULL OR v_column_name = '' THEN
        RAISE EXCEPTION 'Mirrored document relationship trigger requires column name in TG_ARGV[0]'
            USING ERRCODE = '22023';
    END IF;

    IF v_relationship_code IS NULL OR v_relationship_code = '' THEN
        RAISE EXCEPTION 'Mirrored document relationship trigger requires relationship code in TG_ARGV[1]'
            USING ERRCODE = '22023';
    END IF;

    IF TG_OP IN ('UPDATE', 'DELETE') THEN
        v_old_from_document_id := OLD.document_id;
        v_old_to_document_id := nullif(to_jsonb(OLD) ->> v_column_name, '')::uuid;
    END IF;

    IF TG_OP IN ('INSERT', 'UPDATE') THEN
        v_new_from_document_id := NEW.document_id;
        v_new_to_document_id := nullif(to_jsonb(NEW) ->> v_column_name, '')::uuid;
    END IF;

    IF TG_OP = 'UPDATE'
       AND v_old_from_document_id IS NOT DISTINCT FROM v_new_from_document_id
       AND v_old_to_document_id IS NOT DISTINCT FROM v_new_to_document_id THEN
        RETURN NEW;
    END IF;

    IF v_old_from_document_id IS NOT NULL AND v_old_to_document_id IS NOT NULL THEN
        DELETE FROM document_relationships
        WHERE relationship_id = ngb_compute_document_relationship_id(
            v_old_from_document_id,
            v_relationship_code_norm,
            v_old_to_document_id);
    END IF;

    IF v_new_from_document_id IS NOT NULL AND v_new_to_document_id IS NOT NULL THEN
        INSERT INTO document_relationships(
            relationship_id,
            from_document_id,
            to_document_id,
            relationship_code,
            created_at_utc
        )
        VALUES (
            ngb_compute_document_relationship_id(v_new_from_document_id, v_relationship_code_norm, v_new_to_document_id),
            v_new_from_document_id,
            v_new_to_document_id,
            v_relationship_code,
            now())
        ON CONFLICT (relationship_id) DO NOTHING;
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION ngb_install_mirrored_document_relationship_trigger(
    p_table_name text,
    p_column_name text,
    p_relationship_code text
)
RETURNS void AS $$
DECLARE
    v_table_name text := btrim(p_table_name);
    v_column_name text := btrim(p_column_name);
    v_relationship_code text := btrim(p_relationship_code);
    v_trigger_name text;
    v_is_uuid boolean;
BEGIN
    IF v_table_name IS NULL OR v_table_name = '' THEN
        RAISE EXCEPTION 'table_name is required'
            USING ERRCODE = '22023';
    END IF;

    IF v_column_name IS NULL OR v_column_name = '' THEN
        RAISE EXCEPTION 'column_name is required'
            USING ERRCODE = '22023';
    END IF;

    IF v_relationship_code IS NULL OR v_relationship_code = '' THEN
        RAISE EXCEPTION 'relationship_code is required'
            USING ERRCODE = '22023';
    END IF;

    IF to_regclass(format('public.%I', v_table_name)) IS NULL THEN
        RAISE EXCEPTION 'Table public.% does not exist', v_table_name
            USING ERRCODE = '42P01';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = v_table_name
          AND column_name = 'document_id'
    ) THEN
        RAISE EXCEPTION 'Table public.% must have document_id column', v_table_name
            USING ERRCODE = '42703';
    END IF;

    SELECT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = v_table_name
          AND column_name = v_column_name
    ) INTO v_is_uuid;

    IF NOT v_is_uuid THEN
        RAISE EXCEPTION 'Column public.%.% does not exist', v_table_name, v_column_name
            USING ERRCODE = '42703';
    END IF;

    SELECT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = v_table_name
          AND column_name = v_column_name
          AND udt_name = 'uuid'
    ) INTO v_is_uuid;

    IF NOT v_is_uuid THEN
        RAISE EXCEPTION 'Column public.%.% must be uuid', v_table_name, v_column_name
            USING ERRCODE = '42804';
    END IF;

    v_trigger_name := format(
        'trg_docrel_mirror__%s__%s',
        left(lower(regexp_replace(v_column_name, '[^a-zA-Z0-9_]+', '_', 'g')), 20),
        substr(md5(v_column_name || '|' || lower(v_relationship_code)), 1, 8));

    EXECUTE format('DROP TRIGGER IF EXISTS %I ON public.%I;', v_trigger_name, v_table_name);

    EXECUTE format(
        'CREATE TRIGGER %I
             AFTER INSERT OR UPDATE OR DELETE ON public.%I
             FOR EACH ROW
             EXECUTE FUNCTION ngb_sync_mirrored_document_relationship(%L, %L);',
        v_trigger_name,
        v_table_name,
        v_column_name,
        v_relationship_code
    );
END;
$$ LANGUAGE plpgsql;
-- <<< DocumentRelationshipMirroringMigration

-- >>> ReportVariantsMigration
CREATE TABLE IF NOT EXISTS report_variants
(
    report_variant_id UUID PRIMARY KEY,
    report_code TEXT NOT NULL,
    report_code_norm TEXT GENERATED ALWAYS AS (lower(btrim(report_code))) STORED,
    variant_code TEXT NOT NULL,
    variant_code_norm TEXT GENERATED ALWAYS AS (lower(btrim(variant_code))) STORED,
    owner_platform_user_id UUID NULL,
    name TEXT NOT NULL,
    layout_json JSONB NULL,
    filters_json JSONB NULL,
    parameters_json JSONB NULL,
    is_default BOOLEAN NOT NULL DEFAULT FALSE,
    is_shared BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT (now() AT TIME ZONE 'UTC'),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT (now() AT TIME ZONE 'UTC'),
    CONSTRAINT ck_report_variants_report_code_nonempty CHECK (length(trim(report_code)) > 0),
    CONSTRAINT ck_report_variants_variant_code_nonempty CHECK (length(trim(variant_code)) > 0),
    CONSTRAINT ck_report_variants_name_nonempty CHECK (length(trim(name)) > 0),
    CONSTRAINT ck_report_variants_scope_consistency CHECK (
        is_shared = TRUE
        OR owner_platform_user_id IS NOT NULL
    ),
    CONSTRAINT fk_report_variants_owner_platform_user
        FOREIGN KEY (owner_platform_user_id)
        REFERENCES platform_users(user_id)
        ON DELETE RESTRICT
);

ALTER TABLE report_variants
    DROP CONSTRAINT IF EXISTS ck_report_variants_scope_consistency;

ALTER TABLE report_variants
    ADD CONSTRAINT ck_report_variants_scope_consistency CHECK (
        is_shared = TRUE
        OR owner_platform_user_id IS NOT NULL
    );

DROP INDEX IF EXISTS ux_report_variants_report_variant_code;

CREATE UNIQUE INDEX IF NOT EXISTS ux_report_variants_shared_variant_code
    ON report_variants(report_code_norm, variant_code_norm)
    WHERE is_shared = TRUE;

CREATE UNIQUE INDEX IF NOT EXISTS ux_report_variants_private_owner_variant_code
    ON report_variants(report_code_norm, owner_platform_user_id, variant_code_norm)
    WHERE is_shared = FALSE;

CREATE UNIQUE INDEX IF NOT EXISTS ux_report_variants_shared_default
    ON report_variants(report_code_norm)
    WHERE is_shared = TRUE AND is_default = TRUE;

CREATE UNIQUE INDEX IF NOT EXISTS ux_report_variants_private_default
    ON report_variants(report_code_norm, owner_platform_user_id)
    WHERE is_shared = FALSE AND is_default = TRUE;

CREATE INDEX IF NOT EXISTS ix_report_variants_report_visibility
    ON report_variants(report_code_norm, is_shared, owner_platform_user_id, name, variant_code);

CREATE OR REPLACE FUNCTION trg_report_variants_enforce_code_scope_conflicts()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    new_report_code_norm TEXT := lower(btrim(NEW.report_code));
    new_variant_code_norm TEXT := lower(btrim(NEW.variant_code));
BEGIN
    IF NEW.is_shared THEN
        IF EXISTS
        (
            SELECT 1
            FROM report_variants rv
            WHERE rv.report_variant_id <> NEW.report_variant_id
              AND rv.report_code_norm = new_report_code_norm
              AND rv.variant_code_norm = new_variant_code_norm
        ) THEN
            RAISE EXCEPTION 'Report variant code conflict for report=% variant=%', NEW.report_code, NEW.variant_code
                USING ERRCODE = 'unique_violation',
                      CONSTRAINT = 'tr_report_variants_enforce_code_scope_conflicts';
        END IF;
    ELSE
        IF EXISTS
        (
            SELECT 1
            FROM report_variants rv
            WHERE rv.report_variant_id <> NEW.report_variant_id
              AND rv.report_code_norm = new_report_code_norm
              AND rv.variant_code_norm = new_variant_code_norm
              AND
              (
                  rv.is_shared = TRUE
                  OR rv.owner_platform_user_id = NEW.owner_platform_user_id
              )
        ) THEN
            RAISE EXCEPTION 'Report variant code conflict for report=% variant=% owner=%', NEW.report_code, NEW.variant_code, NEW.owner_platform_user_id
                USING ERRCODE = 'unique_violation',
                      CONSTRAINT = 'tr_report_variants_enforce_code_scope_conflicts';
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS tr_report_variants_enforce_code_scope_conflicts ON public.report_variants;

CREATE TRIGGER tr_report_variants_enforce_code_scope_conflicts
    BEFORE INSERT OR UPDATE OF report_code, variant_code, owner_platform_user_id, is_shared
    ON public.report_variants
    FOR EACH ROW
    EXECUTE FUNCTION trg_report_variants_enforce_code_scope_conflicts();
-- <<< ReportVariantsMigration
