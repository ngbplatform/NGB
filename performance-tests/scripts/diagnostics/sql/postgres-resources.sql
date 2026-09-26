-- PostgreSQL 17+. Fixed monitoring query; no business rows or raw SQL text are exported.
SELECT jsonb_build_object(
  'at', clock_timestamp(),
  'read_only', current_setting('transaction_read_only'),
  'database', (SELECT jsonb_build_object(
    'commits', xact_commit, 'rollbacks', xact_rollback,
    'deadlocks', deadlocks, 'temp_bytes', temp_bytes,
    'blocks_read', blks_read, 'blocks_hit', blks_hit,
    'connections', numbackends, 'stats_reset', stats_reset
  ) FROM pg_stat_database WHERE datname = current_database()),
  'checkpoint', (SELECT to_jsonb(c) FROM pg_stat_checkpointer c),
  'wal', (SELECT to_jsonb(w) FROM pg_stat_wal w),
  'vacuum', (SELECT jsonb_agg(to_jsonb(v)) FROM pg_stat_progress_vacuum v
    WHERE datname = current_database()),
  'sessions', (SELECT jsonb_agg(s) FROM (
    SELECT application_name, state, wait_event_type, wait_event, count(*) AS count,
      max(EXTRACT(epoch FROM clock_timestamp() - xact_start)) AS oldest_transaction_seconds
    FROM pg_stat_activity
    WHERE datname = current_database() AND application_name <> 'ngb.perf.postgres-mcp'
    GROUP BY application_name, state, wait_event_type, wait_event
  ) s),
  'transactions', (SELECT jsonb_agg(t) FROM (
    SELECT a.pid, a.application_name, a.state, a.wait_event_type, a.wait_event,
      EXTRACT(epoch FROM clock_timestamp() - a.xact_start) AS transaction_seconds,
      pg_blocking_pids(a.pid) AS blocking_pids,
      CASE
        WHEN a.query LIKE 'WITH RECURSIVE requested%' THEN 'batch_advisory_lock'
        WHEN a.query LIKE '%matching_dimension_sets%' THEN 'resource_net_lookup'
        ELSE 'other'
      END AS query_kind,
      (SELECT jsonb_agg(jsonb_build_object(
        'classid', l.classid, 'objid', l.objid, 'objsubid', l.objsubid,
        'mode', l.mode, 'granted', l.granted
      )) FROM pg_locks l WHERE l.pid = a.pid AND l.locktype = 'advisory') AS advisory_locks
    FROM pg_stat_activity a
    WHERE a.datname = current_database() AND a.application_name <> 'ngb.perf.postgres-mcp'
      AND a.xact_start IS NOT NULL
    ORDER BY a.xact_start LIMIT 200
  ) t)
) AS sample;
