-- Finalization workers poll only Dirty or Blocked rows. Partial indexes keep those
-- queue scans proportional to outstanding work instead of the full history table.
CREATE INDEX IF NOT EXISTS ix_opreg_finalizations_dirty_queue
    ON public.operational_register_finalizations(dirty_since_utc, register_id, period)
    WHERE status = 2;

CREATE INDEX IF NOT EXISTS ix_opreg_finalizations_blocked_queue
    ON public.operational_register_finalizations(blocked_since_utc, register_id, period)
    WHERE status = 3;
