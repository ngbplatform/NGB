-- A fixed, parameterized, read-only MCP tool. Never returns job arguments, errors or business rows.
SELECT jsonb_build_object(
  'at', clock_timestamp(), 'read_only', current_setting('transaction_read_only'),
  'registered', EXISTS(SELECT 1 FROM hangfire.hash WHERE key = 'recurring-job:opreg.finalization.run_dirty_months' AND field = 'Job'),
  'last_job_id', (SELECT value FROM hangfire.hash WHERE key = 'recurring-job:opreg.finalization.run_dirty_months' AND field = 'LastJobId'),
  'live_workers', (SELECT count(*) FROM hangfire.server WHERE lastheartbeat > clock_timestamp() - interval '1 minute'),
  'months_count', (SELECT count(*) FROM public.operational_register_finalizations),
  'months', (SELECT coalesce(jsonb_agg(m), '[]'::jsonb) FROM (
    SELECT r.code, f.period::text, f.status, f.finalized_at_utc, f.dirty_since_utc
    FROM public.operational_register_finalizations f
    JOIN public.operational_registers r USING (register_id)
    ORDER BY r.code, f.period LIMIT 500
  ) m),
  'job', (SELECT jsonb_build_object('id', j.id::text, 'state', j.statename, 'created_at', j.createdat,
      'history', (SELECT coalesce(jsonb_agg(jsonb_build_object('state', s.name, 'at', s.createdat) ORDER BY s.id), '[]'::jsonb)
                  FROM hangfire.state s WHERE s.jobid = j.id))
    FROM hangfire.job j WHERE j.id = coalesce($1::bigint,
      (SELECT value::bigint FROM hangfire.hash WHERE key = 'recurring-job:opreg.finalization.run_dirty_months' AND field = 'LastJobId')))
) AS sample;
