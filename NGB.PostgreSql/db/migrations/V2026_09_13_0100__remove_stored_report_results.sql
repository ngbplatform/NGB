-- Deploy with report-run consumers stopped. Only generated report results are removed.
-- DROP releases their storage directly; no recurring cleanup job or bulk DELETE is needed.
DROP TABLE IF EXISTS platform_report_run_rows;
DROP TABLE IF EXISTS platform_report_runs;
