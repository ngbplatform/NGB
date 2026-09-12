using Dapper;
using NGB.Persistence.Reporting;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Reporting;

public sealed class PostgresReportRunStore(IUnitOfWork uow) : IReportRunStore
{
    private const string Columns = "id, owner, report_code AS ReportCode, prepared_json::text AS PreparedJson, status, attempt, template_json::text AS TemplateJson, row_count AS RowCount, expires_at_utc AS ExpiresAtUtc";

    public Task CreateAsync(Guid id, string owner, string reportCode, string preparedJson, CancellationToken ct)
        => ExecuteAsync("""
            INSERT INTO platform_report_runs(id, owner, report_code, prepared_json, attempt)
            VALUES (@id, @owner, @reportCode, @preparedJson::jsonb, @id);
            """,
            new { id, owner, reportCode, preparedJson },
            ct);

    public async Task<StoredReportRun?> FindAsync(Guid id, string owner, string reportCode, CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        return await uow.Connection.QuerySingleOrDefaultAsync<StoredReportRun>(new CommandDefinition(
            $"SELECT {Columns} FROM platform_report_runs WHERE id=@id AND owner=@owner AND report_code=@reportCode AND expires_at_utc > now()",
            new { id, owner, reportCode },
            cancellationToken: ct));
    }

    public async Task<StoredReportRun?> ClaimAsync(string[] reportCodes, CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        return await uow.Connection.QuerySingleOrDefaultAsync<StoredReportRun>(new CommandDefinition($"""
            WITH candidate AS (
                SELECT id FROM platform_report_runs
                WHERE report_code = ANY(@reportCodes) AND expires_at_utc > now() AND attempts < 3
                  AND (status = 'Queued' OR (status = 'Running' AND lease_until_utc < now()))
                ORDER BY created_at_utc, id FOR UPDATE SKIP LOCKED LIMIT 1
            )
            UPDATE platform_report_runs r SET status='Running', attempt=@attempt, attempts=attempts+1,
                lease_until_utc=now()+interval '30 seconds'
            FROM candidate c WHERE r.id=c.id RETURNING {Columns.Replace("id, owner", "r.id, owner")};
            """,
            new { reportCodes, attempt = Guid.CreateVersion7() },
            cancellationToken: ct));
    }

    public async Task<bool> RenewAsync(Guid id, Guid attempt, CancellationToken ct)
        => await ExecuteAsync("""
            UPDATE platform_report_runs SET lease_until_utc=now()+interval '30 seconds'
            WHERE id=@id AND attempt=@attempt AND status='Running' AND expires_at_utc > now();
            """,
            new { id, attempt },
            ct) == 1;

    public Task AppendAsync(Guid id, Guid attempt, int offset, string[] rows, CancellationToken ct)
        => ExecuteAsync("""
            INSERT INTO platform_report_run_rows(run_id, attempt, ordinal, row_json)
            SELECT @id, @attempt, @offset + (r.n-1)::integer, r.json::jsonb
            FROM unnest(@rows::text[]) WITH ORDINALITY r(json,n)
            WHERE EXISTS (SELECT 1 FROM platform_report_runs WHERE id=@id AND attempt=@attempt AND status='Running' AND expires_at_utc > now() FOR UPDATE);
            """,
            new { id, attempt, offset, rows },
            ct);

    public async Task<bool> CompleteAsync(Guid id, Guid attempt, string templateJson, int count, CancellationToken ct)
        => await ExecuteAsync("""
            UPDATE platform_report_runs SET status='Ready', template_json=@templateJson::jsonb, row_count=@count,
                expires_at_utc=now()+interval '1 day', lease_until_utc=NULL
            WHERE id=@id AND attempt=@attempt AND status='Running' AND expires_at_utc > now()
              AND (SELECT count(*)=@count AND (@count=0 OR (min(ordinal)=0 AND max(ordinal)=@count-1))
                   FROM platform_report_run_rows WHERE run_id=@id AND attempt=@attempt);
            """,
            new { id, attempt, templateJson, count },
            ct) == 1;

    public Task WriteAsync(Guid id, Guid attempt, int[] ordinals, string[] rows, CancellationToken ct)
        => ExecuteAsync("""
            INSERT INTO platform_report_run_rows(run_id, attempt, ordinal, row_json)
            SELECT @id, @attempt, r.ordinal, r.json::jsonb FROM unnest(@ordinals::integer[], @rows::text[]) r(ordinal,json)
            WHERE EXISTS (SELECT 1 FROM platform_report_runs WHERE id=@id AND attempt=@attempt AND status='Running' AND expires_at_utc > now() FOR UPDATE)
            ON CONFLICT (run_id, attempt, ordinal) DO UPDATE SET row_json=EXCLUDED.row_json;
            """,
            new { id, attempt, ordinals, rows },
            ct);

    public Task FailAsync(Guid id, Guid attempt, CancellationToken ct) => FailAsync(id, attempt, null, ct);

    public Task FailAsync(Guid id, Guid attempt, string? failureJson, CancellationToken ct)
        => ExecuteAsync(
            "UPDATE platform_report_runs SET status='Failed',template_json=@failureJson::jsonb,lease_until_utc=NULL WHERE id=@id AND attempt=@attempt AND status='Running'",
            new { id, attempt, failureJson },
            ct);

    public Task CancelAsync(Guid id, string owner, string reportCode, CancellationToken ct)
        => ExecuteAsync("""
            UPDATE platform_report_runs SET status='Cancelled',lease_until_utc=NULL
            WHERE id=@id AND owner=@owner AND report_code=@reportCode AND status IN ('Queued','Running');
            """,
            new { id, owner, reportCode },
            ct);

    public async Task<IReadOnlyList<string>> ReadRowsAsync(
        Guid id,
        Guid attempt,
        int offset,
        int limit,
        CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        return (await uow.Connection.QueryAsync<string>(new CommandDefinition("""
            SELECT row_json::text FROM platform_report_run_rows
            WHERE run_id=@id AND attempt=@attempt AND ordinal>=@offset ORDER BY ordinal LIMIT @limit;
            """,
            new { id, attempt, offset, limit },
            cancellationToken: ct)))
            .AsList();
    }

    public async Task CleanupAsync(CancellationToken ct)
    {
        await ExecuteAsync("""
            UPDATE platform_report_runs SET status='Failed',lease_until_utc=NULL
            WHERE status='Running' AND lease_until_utc < now() AND attempts>=3;
            DELETE FROM platform_report_run_rows WHERE (run_id,attempt,ordinal) IN (
                SELECT x.run_id,x.attempt,x.ordinal FROM platform_report_run_rows x
                JOIN platform_report_runs r ON r.id=x.run_id
                WHERE r.expires_at_utc < now() OR x.attempt<>r.attempt OR r.status IN ('Failed','Cancelled') LIMIT 1000);
            DELETE FROM platform_report_runs WHERE id IN (
                SELECT r.id FROM platform_report_runs r WHERE r.expires_at_utc < now()
                  AND NOT EXISTS (SELECT 1 FROM platform_report_run_rows x WHERE x.run_id=r.id)
                ORDER BY r.expires_at_utc LIMIT 100);
            """,
            null,
            ct);
    }

    private async Task<int> ExecuteAsync(string sql, object? args, CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        return await uow.Connection.ExecuteAsync(new CommandDefinition(sql, args, cancellationToken: ct));
    }
}
