using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

/// <summary>
/// Defense in depth: forbid INSERT/UPDATE/DELETE into accounting_register_main, accounting_turnovers
/// and accounting_balances for closed periods.
///
/// PostingEngine and PeriodClosingService already enforce this at runtime, but these triggers protect
/// data integrity even if someone tries to bypass the app and write SQL directly.
/// </summary>
public sealed class AccountingClosedPeriodsGuardMigration : IDdlObject
{
    public string Name => "accounting_closed_periods_guard";

    public string Generate() => """
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
                                """;
}
