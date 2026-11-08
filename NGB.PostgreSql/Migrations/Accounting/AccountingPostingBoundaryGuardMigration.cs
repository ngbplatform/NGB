using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingPostingBoundaryGuardMigration : IDdlObject
{
    public string Name => "accounting_posting_boundary_guard";

    public string Generate() => """
        -- Protect closed snapshots from accounting movements dated before the closed chain.
        -- ACH\x01 / 1 is shared with AdvisoryLockNamespaces.AccountingHistory.
        CREATE OR REPLACE FUNCTION ngb_forbid_posting_into_closed_period()
        RETURNS trigger AS $$
        DECLARE
            p DATE;
            old_p DATE;
            closed_p DATE;
        BEGIN
            IF TG_OP <> 'INSERT' THEN
                IF pg_typeof(OLD.period) = 'date'::regtype THEN
                    old_p := date_trunc('month', OLD.period::timestamp)::date;
                ELSE
                    old_p := date_trunc('month', OLD.period AT TIME ZONE 'UTC')::date;
                END IF;
            END IF;

            IF TG_OP = 'DELETE' THEN
                p := old_p;
            ELSE
                IF pg_typeof(NEW.period) = 'date'::regtype THEN
                    p := date_trunc('month', NEW.period::timestamp)::date;
                ELSE
                    p := date_trunc('month', NEW.period AT TIME ZONE 'UTC')::date;
                END IF;
                IF TG_TABLE_NAME <> 'accounting_balances' THEN
                    p := LEAST(p, old_p);
                END IF;
            END IF;

            -- A first/earlier register movement can change the starting month of the closed chain.
            -- Runtime takes this lock before month locks; this also protects direct register inserts.
            IF TG_TABLE_NAME = 'accounting_register_main' AND TG_OP <> 'DELETE'
               AND NOT EXISTS (SELECT 1 FROM accounting_register_main WHERE period_month <= p) THEN
                PERFORM pg_advisory_xact_lock_shared(1094928385, 1);
            END IF;

            IF TG_TABLE_NAME = 'accounting_balances' THEN
                -- Balance snapshots are derived data. Preserve the existing exact-month protection
                -- so open-month rebuild/repair does not change the business posting boundary.
                SELECT period INTO closed_p
                FROM accounting_closed_periods
                WHERE period = p OR period = old_p
                ORDER BY period
                LIMIT 1;
            ELSE
                SELECT period INTO closed_p
                FROM accounting_closed_periods
                WHERE period >= p
                ORDER BY period
                LIMIT 1;
            END IF;

            IF closed_p IS NOT NULL THEN
                IF closed_p = p OR closed_p = old_p THEN
                    RAISE EXCEPTION 'Posting is forbidden. Period is closed: %', closed_p;
                END IF;
                RAISE EXCEPTION 'Posting into % is forbidden because a later period is closed: %. Reopen the dependent closed periods first.', p, closed_p;
            END IF;

            IF TG_OP = 'DELETE' THEN
                RETURN OLD;
            END IF;
            RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;

        CREATE OR REPLACE FUNCTION ngb_lock_closed_period_history()
        RETURNS trigger AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock(1094928385, 1);
            RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;

        DROP TRIGGER IF EXISTS trg_acc_closed_period_history ON accounting_closed_periods;
        CREATE TRIGGER trg_acc_closed_period_history
            BEFORE INSERT ON accounting_closed_periods
            FOR EACH ROW EXECUTE FUNCTION ngb_lock_closed_period_history();
        """;
}
