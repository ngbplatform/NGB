using Dapper;
using System.Text;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresPostingStateReader(IUnitOfWork uow) : IPostingStateReader
{
    public async Task<PostingStatePage> GetPageAsync(PostingStatePageRequest request, CancellationToken ct = default)
    {
        // Contract: reader is lenient for bounds coming from UI/JSON (Unspecified => treat as UTC).
        var bounds = request.NormalizeForQuery(
            PostingStatePageRequestNormalization.UtcBoundsPolicy.LenientAssumeUtc);

        var staleAfter = bounds.StaleAfter;
        var nowUtc = bounds.NowUtc;
        var fromUtc = bounds.FromUtc;
        var toUtc = bounds.ToUtc;
        var staleCutoffUtc = nowUtc - staleAfter;

        var pagingEnabled = !request.DisablePaging;
        var limit = pagingEnabled ? request.PageSize + 1 : 0;

        // Keyset paging ordered by started_at_utc DESC, document_id DESC, operation DESC.
        var afterStarted = pagingEnabled ? request.Cursor?.AfterStartedAtUtc : null;
        var afterDoc = pagingEnabled ? request.Cursor?.AfterDocumentId ?? Guid.Empty : Guid.Empty;
        var afterOp = pagingEnabled ? request.Cursor?.AfterOperation ?? 0 : 0;

        var sql = new StringBuilder("""
                                    SELECT
                                        l.document_id       AS "DocumentId",
                                        l.operation         AS "Operation",
                                        l.started_at_utc    AS "StartedAtUtc",
                                        l.completed_at_utc  AS "CompletedAtUtc",
                                        CASE
                                            WHEN l.completed_at_utc IS NOT NULL THEN 2
                                            WHEN l.started_at_utc < CAST(@StaleCutoffUtc AS timestamptz) THEN 3
                                            ELSE 1
                                        END                 AS "Status"
                                    FROM accounting_posting_state l
                                    WHERE
                                        l.started_at_utc >= CAST(@FromUtc AS timestamptz)
                                        AND l.started_at_utc <= CAST(@ToUtc AS timestamptz)
                                    """);

        if (request.DocumentId.HasValue)
            sql.AppendLine("  AND l.document_id = CAST(@DocumentId AS uuid)");

        if (request.Operation.HasValue)
            sql.AppendLine("  AND l.operation = CAST(@Operation AS smallint)");

        switch (request.Status)
        {
            case PostingStateStatus.Completed:
                sql.AppendLine("  AND l.completed_at_utc IS NOT NULL");
                break;
            case PostingStateStatus.InProgress:
                sql.AppendLine("  AND l.completed_at_utc IS NULL");
                sql.AppendLine("  AND l.started_at_utc >= CAST(@StaleCutoffUtc AS timestamptz)");
                break;
            case PostingStateStatus.StaleInProgress:
                sql.AppendLine("  AND l.completed_at_utc IS NULL");
                sql.AppendLine("  AND l.started_at_utc < CAST(@StaleCutoffUtc AS timestamptz)");
                break;
        }

        if (afterStarted.HasValue)
            sql.AppendLine("  AND (l.started_at_utc, l.document_id, l.operation) < (CAST(@AfterStarted AS timestamptz), CAST(@AfterDoc AS uuid), CAST(@AfterOp AS smallint))");

        sql.AppendLine("ORDER BY l.started_at_utc DESC, l.document_id DESC, l.operation DESC");
        if (pagingEnabled)
            sql.AppendLine("LIMIT @Limit;");

        await uow.EnsureConnectionOpenAsync(ct);

        var rows = (await uow.Connection.QueryAsync<PostingStateRow>(
            new CommandDefinition(
                sql.ToString(),
                new
                {
                    FromUtc = fromUtc,
                    ToUtc = toUtc,
                    StaleCutoffUtc = staleCutoffUtc,
                    DocumentId = request.DocumentId,
                    Operation = request.Operation is null ? null : (short?)request.Operation.Value,
                    AfterStarted = afterStarted,
                    AfterDoc = afterDoc,
                    AfterOp = afterOp,
                    Limit = limit
                },
                uow.Transaction,
                cancellationToken: ct))).AsList();

        var hasMore = pagingEnabled && rows.Count > request.PageSize;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var records = rows.Select(r =>
        {
            var op = (PostingOperation)r.Operation;
            var status = (PostingStateStatus)r.Status;

            TimeSpan? duration = r.CompletedAtUtc is null ? null : (r.CompletedAtUtc.Value - r.StartedAtUtc);
            var age = (r.CompletedAtUtc ?? nowUtc) - r.StartedAtUtc;

            return new PostingStateRecord(
                r.DocumentId,
                op,
                r.StartedAtUtc,
                r.CompletedAtUtc,
                status,
                duration,
                age);
        }).ToList();

        PostingStateCursor? nextCursor = null;
        if (hasMore && rows.Count > 0)
        {
            var last = rows[^1];
            nextCursor = new PostingStateCursor(last.StartedAtUtc, last.DocumentId, last.Operation);
        }

        return new PostingStatePage(records, hasMore, nextCursor);
    }

    private sealed class PostingStateRow
    {
        public Guid DocumentId { get; init; }
        public short Operation { get; init; }
        public DateTime StartedAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
        public int Status { get; init; }
    }
}
