using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

public sealed class PlatformReportRunsMigration : IDdlObject
{
    public string Name => "platform_report_runs";

    public string Generate() => """
        CREATE TABLE IF NOT EXISTS platform_report_runs (
            id uuid PRIMARY KEY,
            owner text NOT NULL CHECK (length(owner) BETWEEN 1 AND 500),
            report_code text NOT NULL,
            prepared_json jsonb NOT NULL,
            status text NOT NULL DEFAULT 'Queued' CHECK (status IN ('Queued','Running','Ready','Failed','Cancelled')),
            attempt uuid NOT NULL,
            attempts integer NOT NULL DEFAULT 0,
            lease_until_utc timestamptz,
            template_json jsonb,
            row_count integer NOT NULL DEFAULT 0 CHECK (row_count >= 0),
            created_at_utc timestamptz NOT NULL DEFAULT now(),
            expires_at_utc timestamptz NOT NULL DEFAULT now() + interval '1 day'
        );
        CREATE INDEX IF NOT EXISTS ix_platform_report_runs_pending
            ON platform_report_runs(created_at_utc, id) WHERE status IN ('Queued','Running');
        CREATE INDEX IF NOT EXISTS ix_platform_report_runs_expiry ON platform_report_runs(expires_at_utc);
        CREATE TABLE IF NOT EXISTS platform_report_run_rows (
            run_id uuid NOT NULL REFERENCES platform_report_runs(id) ON DELETE CASCADE,
            attempt uuid NOT NULL,
            ordinal integer NOT NULL CHECK (ordinal >= 0),
            row_json jsonb NOT NULL,
            PRIMARY KEY (run_id, attempt, ordinal)
        );
        """;
}
