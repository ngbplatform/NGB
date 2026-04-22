using Dapper;
using NGB.Accounting.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Idempotency;

/// <summary>
/// Shared helper for idempotency state tables with the common schema:
///   (key columns...) + attempt_id + started_at_utc + completed_at_utc
///
/// Pattern:
/// - INSERT .. ON CONFLICT DO NOTHING
/// - If exists:
///   - return AlreadyCompleted when completed_at_utc is not null
///   - if started_at_utc is older than timeout: attempt atomic takeover by replacing attempt_id + started_at_utc
/// - Otherwise: return InProgress
///
/// History:
/// - Optional append-only history tables capture Started / Completed / Superseded events per attempt.
/// - This keeps the state table ephemeral while preserving immutable technical history.
///
/// IMPORTANT:
/// - MUST be used within an active transaction (<see cref="IUnitOfWork.Transaction"/>).
/// </summary>
internal static class PostgresIdempotencyLog
{
    internal static readonly TimeSpan DefaultInProgressTimeout = TimeSpan.FromMinutes(10);

    internal readonly record struct Key(string Column, string Param, object Value);

    private enum HistoryEventKind : short
    {
        Started = 1,
        Completed = 2,
        Superseded = 3
    }

    private sealed class LogRow
    {
        public Guid? AttemptId { get; init; }
        public DateTime StartedAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
    }

    internal static async Task<PostingStateBeginResult> TryBeginAsync(
        IUnitOfWork uow,
        string table,
        string? historyTable,
        IReadOnlyList<Key> keys,
        DateTime startedAtUtc,
        TimeSpan? inProgressTimeout,
        Func<string> notFoundMessage,
        CancellationToken ct)
    {
        await uow.EnsureOpenForTransactionAsync(ct);
        startedAtUtc.EnsureUtc(nameof(startedAtUtc));

        if (string.IsNullOrWhiteSpace(table))
            throw new NgbArgumentRequiredException(nameof(table));

        if (keys is null || keys.Count == 0)
            throw new NgbArgumentRequiredException(nameof(keys));

        var timeout = inProgressTimeout ?? DefaultInProgressTimeout;
        var attemptId = Guid.CreateVersion7();

        var columns = string.Join(", ", keys.Select(k => k.Column));
        var values = string.Join(", ", keys.Select(k => "@" + k.Param));
        var predicates = string.Join(" AND ", keys.Select(k => $"{k.Column} = @{k.Param}"));

        var p = new DynamicParameters();
        foreach (var k in keys)
            p.Add(k.Param, k.Value);

        p.Add("AttemptId", attemptId);
        p.Add("StartedAtUtc", startedAtUtc);

        var insertSql = $"""
                        INSERT INTO {table}(
                            {columns}, attempt_id, started_at_utc, completed_at_utc
                        )
                        VALUES ({values}, @AttemptId, @StartedAtUtc, NULL)
                        ON CONFLICT ({columns}) DO NOTHING;
                        """;

        var insertCmd = new CommandDefinition(insertSql, p, transaction: uow.Transaction, cancellationToken: ct);
        var inserted = await uow.Connection.ExecuteAsync(insertCmd);

        if (inserted == 1)
        {
            await InsertHistoryEventAsync(uow, historyTable, keys, attemptId, HistoryEventKind.Started, startedAtUtc, ct);
            return PostingStateBeginResult.Begun;
        }

        var selectSql = $"""
                        SELECT attempt_id AS "AttemptId",
                               started_at_utc AS "StartedAtUtc",
                               completed_at_utc AS "CompletedAtUtc"
                        FROM {table}
                        WHERE {predicates};
                        """;

        var selectCmd = new CommandDefinition(selectSql, p, transaction: uow.Transaction, cancellationToken: ct);
        var row = await uow.Connection.QuerySingleOrDefaultAsync<LogRow>(selectCmd);

        if (row is null)
            throw new NgbInvariantViolationException(notFoundMessage());

        if (row.CompletedAtUtc is not null)
            return PostingStateBeginResult.AlreadyCompleted;

        var cutoffUtc = startedAtUtc - timeout;
        if (row.StartedAtUtc < cutoffUtc)
        {
            p.Add("OldStartedAtUtc", row.StartedAtUtc);
            p.Add("OldAttemptId", row.AttemptId);

            var takeoverSql = $"""
                              UPDATE {table}
                              SET attempt_id = @AttemptId,
                                  started_at_utc = @StartedAtUtc
                              WHERE {predicates}
                                AND completed_at_utc IS NULL
                                AND started_at_utc = @OldStartedAtUtc
                                AND attempt_id IS NOT DISTINCT FROM @OldAttemptId;
                              """;

            var takeoverCmd = new CommandDefinition(takeoverSql, p, transaction: uow.Transaction, cancellationToken: ct);
            var taken = await uow.Connection.ExecuteAsync(takeoverCmd);

            if (taken == 1)
            {
                if (row.AttemptId.HasValue)
                    await InsertHistoryEventAsync(uow, historyTable, keys, row.AttemptId.Value, HistoryEventKind.Superseded, startedAtUtc, ct);

                await InsertHistoryEventAsync(uow, historyTable, keys, attemptId, HistoryEventKind.Started, startedAtUtc, ct);
                return PostingStateBeginResult.Begun;
            }

            var reread = await uow.Connection.QuerySingleOrDefaultAsync<LogRow>(selectCmd);
            if (reread?.CompletedAtUtc is not null)
                return PostingStateBeginResult.AlreadyCompleted;
        }

        return PostingStateBeginResult.InProgress;
    }

    internal static async Task MarkCompletedAsync(
        IUnitOfWork uow,
        string table,
        string? historyTable,
        IReadOnlyList<Key> keys,
        DateTime completedAtUtc,
        Func<string> multiRowMessage,
        IDictionary<string, object?>? context,
        CancellationToken ct)
    {
        await uow.EnsureOpenForTransactionAsync(ct);
        completedAtUtc.EnsureUtc(nameof(completedAtUtc));

        if (string.IsNullOrWhiteSpace(table))
            throw new NgbArgumentRequiredException(nameof(table));

        if (keys is null || keys.Count == 0)
            throw new NgbArgumentRequiredException(nameof(keys));

        var predicates = string.Join(" AND ", keys.Select(k => $"{k.Column} = @{k.Param}"));

        var p = new DynamicParameters();
        foreach (var k in keys)
            p.Add(k.Param, k.Value);

        p.Add("CompletedAtUtc", completedAtUtc);

        var sql = $"""
                  WITH updated AS (
                      UPDATE {table}
                      SET completed_at_utc = @CompletedAtUtc
                      WHERE {predicates}
                        AND completed_at_utc IS NULL
                      RETURNING attempt_id
                  )
                  SELECT attempt_id AS "AttemptId"
                  FROM updated;
                  """;

        var cmd = new CommandDefinition(sql, p, transaction: uow.Transaction, cancellationToken: ct);
        var rows = (await uow.Connection.QueryAsync<Guid?>(cmd)).ToArray();

        if (rows.Length > 1)
        {
            var ctx = context is not null
                ? new Dictionary<string, object?>(context)
                : new Dictionary<string, object?>();

            ctx.TryAdd("rows", rows.Length);
            throw new NgbInvariantViolationException(multiRowMessage(), ctx);
        }

        if (rows.Length == 1 && rows[0].HasValue)
            await InsertHistoryEventAsync(uow, historyTable, keys, rows[0]!.Value, HistoryEventKind.Completed, completedAtUtc, ct);
    }

    private static async Task InsertHistoryEventAsync(
        IUnitOfWork uow,
        string? historyTable,
        IReadOnlyList<Key> keys,
        Guid attemptId,
        HistoryEventKind eventKind,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(historyTable))
            return;

        var columns = string.Join(", ", keys.Select(k => k.Column));
        var values = string.Join(", ", keys.Select(k => "@" + k.Param));

        var p = new DynamicParameters();
        foreach (var k in keys)
            p.Add(k.Param, k.Value);

        p.Add("HistoryId", Guid.CreateVersion7());
        p.Add("AttemptId", attemptId);
        p.Add("EventKind", (short)eventKind);
        p.Add("OccurredAtUtc", occurredAtUtc);

        var sql = $"""
                  INSERT INTO {historyTable} (
                      history_id,
                      attempt_id,
                      {columns},
                      event_kind,
                      occurred_at_utc
                  )
                  VALUES (
                      @HistoryId,
                      @AttemptId,
                      {values},
                      @EventKind,
                      @OccurredAtUtc
                  );
                  """;

        var cmd = new CommandDefinition(sql, p, transaction: uow.Transaction, cancellationToken: ct);
        await uow.Connection.ExecuteAsync(cmd);
    }
}
