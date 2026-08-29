-- These indexes duplicate primary-key or unique-constraint indexes byte for byte.
-- Keeping both copies adds write amplification, WAL volume, vacuum work, and disk usage
-- without adding a distinct access path.
DROP INDEX IF EXISTS public.ix_acc_balances_period_account;
DROP INDEX IF EXISTS public.ix_acc_turnovers_period_account;
DROP INDEX IF EXISTS public.ix_opreg_dim_rules_register_ordinal;
DROP INDEX IF EXISTS public.ix_opreg_finalizations_register_period;
DROP INDEX IF EXISTS public.ix_platform_audit_event_changes_event;
DROP INDEX IF EXISTS public.ix_refreg_dim_rules_register_ordinal;
