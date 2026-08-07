using Dapper;
using NGB.Core.Events;
using NGB.Persistence.Outbox;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Outbox;

public sealed class PostgresOutboxEventRepository(IUnitOfWork uow) : IOutboxEventRepository
{
    public async Task AppendAsync(
        PlatformOutboxEvent outboxEvent,
        IReadOnlyList<string> consumerCodes,
        CancellationToken ct)
    {
        if (outboxEvent is null)
            throw new NgbArgumentRequiredException(nameof(outboxEvent));

        if (consumerCodes is null || consumerCodes.Count == 0)
            throw new NgbArgumentRequiredException(nameof(consumerCodes));

        outboxEvent.OccurredAtUtc.EnsureUtc(nameof(outboxEvent.OccurredAtUtc));
        outboxEvent.CreatedAtUtc.EnsureUtc(nameof(outboxEvent.CreatedAtUtc));
        await uow.EnsureOpenForTransactionAsync(ct);

        const string eventSql = """
            INSERT INTO platform_outbox_events (
                event_id, event_type, schema_version, occurred_at_utc,
                source, subject, actor_user_id, correlation_id, causation_id,
                payload_json, created_at_utc
            )
            VALUES (
                @EventId, @EventType, @SchemaVersion, @OccurredAtUtc,
                @Source, @Subject, @ActorUserId, @CorrelationId, @CausationId,
                CAST(@PayloadJson AS jsonb), @CreatedAtUtc
            );
            """;

        await uow.Connection.ExecuteAsync(new CommandDefinition(eventSql, outboxEvent, uow.Transaction, cancellationToken: ct));

        const string stateSql = """
            INSERT INTO platform_outbox_consumer_state (
                event_id, consumer_code, status, attempt_count, next_attempt_at_utc
            )
            VALUES (@EventId, @ConsumerCode, @PendingStatus, 0, @NextAttemptAtUtc)
            ON CONFLICT (event_id, consumer_code) DO NOTHING;
            """;

        foreach (var consumerCode in consumerCodes
             .Where(static x => !string.IsNullOrWhiteSpace(x))
             .Select(static x => x.Trim())
             .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    stateSql,
                    new
                    {
                        outboxEvent.EventId,
                        ConsumerCode = consumerCode,
                        PendingStatus = (short)OutboxConsumerStatus.Pending,
                        NextAttemptAtUtc = outboxEvent.CreatedAtUtc
                    },
                    uow.Transaction,
                    cancellationToken: ct));
        }
    }

    public async Task<IReadOnlyList<OutboxConsumerWorkItem>> ClaimBatchAsync(
        string consumerCode,
        int batchSize,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(consumerCode))
            throw new NgbArgumentRequiredException(nameof(consumerCode));

        if (batchSize is < 1 or > 500)
            throw new NgbArgumentInvalidException(nameof(batchSize), "Batch size must be between 1 and 500.");

        nowUtc.EnsureUtc(nameof(nowUtc));
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
            WITH candidates AS (
                SELECT s.event_id, s.consumer_code
                FROM platform_outbox_consumer_state s
                WHERE s.consumer_code = @ConsumerCode
                  AND (
                    (s.status IN (@PendingStatus, @FailedStatus) AND s.next_attempt_at_utc <= @NowUtc)
                    OR
                    (s.status = @ProcessingStatus AND s.locked_at_utc < @StaleBeforeUtc)
                  )
                ORDER BY s.next_attempt_at_utc, s.event_id
                FOR UPDATE SKIP LOCKED
                LIMIT @BatchSize
            ),
            claimed AS (
                UPDATE platform_outbox_consumer_state s
                SET status = @ProcessingStatus,
                    attempt_count = s.attempt_count + 1,
                    locked_at_utc = @NowUtc,
                    last_error = NULL
                FROM candidates c
                WHERE s.event_id = c.event_id
                  AND s.consumer_code = c.consumer_code
                RETURNING s.event_id, s.consumer_code, s.attempt_count
            )
            SELECT e.event_id AS "EventId",
                   e.event_type AS "EventType",
                   e.schema_version AS "SchemaVersion",
                   e.occurred_at_utc AS "OccurredAtUtc",
                   e.source AS "Source",
                   e.subject AS "Subject",
                   e.actor_user_id AS "ActorUserId",
                   e.correlation_id AS "CorrelationId",
                   e.causation_id AS "CausationId",
                   e.payload_json::text AS "PayloadJson",
                   e.created_at_utc AS "CreatedAtUtc",
                   c.consumer_code AS "ConsumerCode",
                   c.attempt_count AS "AttemptCount"
            FROM claimed c
            JOIN platform_outbox_events e ON e.event_id = c.event_id
            ORDER BY e.occurred_at_utc, e.event_id;
            """;

        var rows = await uow.Connection.QueryAsync<ClaimedRow>(
            new CommandDefinition(
                sql,
                new
                {
                    ConsumerCode = consumerCode.Trim(),
                    PendingStatus = (short)OutboxConsumerStatus.Pending,
                    ProcessingStatus = (short)OutboxConsumerStatus.Processing,
                    FailedStatus = (short)OutboxConsumerStatus.Failed,
                    NowUtc = nowUtc,
                    StaleBeforeUtc = nowUtc - TimeSpan.FromMinutes(10),
                    BatchSize = batchSize
                },
                uow.Transaction,
                cancellationToken: ct));

        return rows.Select(static row => new OutboxConsumerWorkItem(
                new PlatformOutboxEvent(
                    row.EventId,
                    row.EventType,
                    row.SchemaVersion,
                    row.OccurredAtUtc,
                    row.Source,
                    row.Subject,
                    row.ActorUserId,
                    row.CorrelationId,
                    row.CausationId,
                    row.PayloadJson,
                    row.CreatedAtUtc),
                row.ConsumerCode,
                row.AttemptCount))
            .ToArray();
    }

    public Task MarkCompletedAsync(
        Guid eventId,
        string consumerCode,
        int attemptNumber,
        DateTime completedAtUtc,
        CancellationToken ct)
        => FinishAsync(
            eventId,
            consumerCode,
            attemptNumber,
            completedAtUtc,
            OutboxConsumerStatus.Completed,
            nextAttemptAtUtc: null,
            error: null,
            historyOutcome: 1,
            ct);

    public Task MarkFailedAsync(
        Guid eventId,
        string consumerCode,
        int attemptNumber,
        DateTime completedAtUtc,
        DateTime? nextAttemptAtUtc,
        string sanitizedError,
        bool deadLetter,
        CancellationToken ct)
        => FinishAsync(
            eventId,
            consumerCode,
            attemptNumber,
            completedAtUtc,
            deadLetter ? OutboxConsumerStatus.DeadLetter : OutboxConsumerStatus.Failed,
            nextAttemptAtUtc,
            SanitizeError(sanitizedError),
            historyOutcome: deadLetter ? (short)3 : (short)2,
            ct);

    public async Task<(long PendingCount, DateTime? OldestOccurredAtUtc, long FailedCount)> GetHealthAsync(
        string consumerCode,
        CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
            SELECT count(*) FILTER (WHERE s.status IN (@PendingStatus, @FailedStatus)) AS "PendingCount",
                   min(e.occurred_at_utc) FILTER (WHERE s.status IN (@PendingStatus, @FailedStatus)) AS "OldestOccurredAtUtc",
                   count(*) FILTER (WHERE s.status IN (@FailedStatus, @DeadLetterStatus)) AS "FailedCount"
            FROM platform_outbox_consumer_state s
            JOIN platform_outbox_events e ON e.event_id = s.event_id
            WHERE s.consumer_code = @ConsumerCode;
            """;

        var row = await uow.Connection.QuerySingleAsync<HealthRow>(
            new CommandDefinition(
                sql,
                new
                {
                    ConsumerCode = consumerCode,
                    PendingStatus = (short)OutboxConsumerStatus.Pending,
                    FailedStatus = (short)OutboxConsumerStatus.Failed,
                    DeadLetterStatus = (short)OutboxConsumerStatus.DeadLetter
                },
                uow.Transaction,
                cancellationToken: ct));

        return (row.PendingCount, row.OldestOccurredAtUtc, row.FailedCount);
    }

    private async Task FinishAsync(
        Guid eventId,
        string consumerCode,
        int attemptNumber,
        DateTime completedAtUtc,
        OutboxConsumerStatus status,
        DateTime? nextAttemptAtUtc,
        string? error,
        short historyOutcome,
        CancellationToken ct)
    {
        completedAtUtc.EnsureUtc(nameof(completedAtUtc));
        if (nextAttemptAtUtc is not null)
            nextAttemptAtUtc.Value.EnsureUtc(nameof(nextAttemptAtUtc));

        await uow.EnsureOpenForTransactionAsync(ct);

        const string updateSql = """
            UPDATE platform_outbox_consumer_state
            SET status = @Status,
                next_attempt_at_utc = COALESCE(@NextAttemptAtUtc, next_attempt_at_utc),
                completed_at_utc = CASE WHEN @Status = @CompletedStatus THEN @CompletedAtUtc ELSE NULL END,
                last_error = @LastError
            WHERE event_id = @EventId
              AND consumer_code = @ConsumerCode
              AND status = @ProcessingStatus
              AND attempt_count = @AttemptNumber
            RETURNING locked_at_utc;
            """;

        var startedAtUtc = await uow.Connection.QuerySingleOrDefaultAsync<DateTime?>(
            new CommandDefinition(
                updateSql,
                new
                {
                    EventId = eventId,
                    ConsumerCode = consumerCode,
                    Status = (short)status,
                    CompletedStatus = (short)OutboxConsumerStatus.Completed,
                    ProcessingStatus = (short)OutboxConsumerStatus.Processing,
                    AttemptNumber = attemptNumber,
                    CompletedAtUtc = completedAtUtc,
                    NextAttemptAtUtc = nextAttemptAtUtc,
                    LastError = error
                },
                uow.Transaction,
                cancellationToken: ct));

        if (startedAtUtc is null)
            throw new NgbInvariantViolationException("Outbox consumer state could not be completed for the claimed attempt.");

        const string historySql = """
            INSERT INTO platform_outbox_consumer_history (
                history_id, event_id, consumer_code, attempt_number,
                started_at_utc, completed_at_utc, outcome, error_metadata
            )
            VALUES (
                @HistoryId, @EventId, @ConsumerCode, @AttemptNumber,
                @StartedAtUtc, @CompletedAtUtc, @Outcome, @Error
            );
            """;

        await uow.Connection.ExecuteAsync(
            new CommandDefinition(
                historySql,
                new
                {
                    HistoryId = Guid.CreateVersion7(),
                    EventId = eventId,
                    ConsumerCode = consumerCode,
                    AttemptNumber = attemptNumber,
                    StartedAtUtc = startedAtUtc.Value,
                    CompletedAtUtc = completedAtUtc,
                    Outcome = historyOutcome,
                    Error = error
                },
                uow.Transaction,
                cancellationToken: ct));
    }

    private static string SanitizeError(string value)
    {
        var sanitized = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Length <= 2000 ? sanitized : sanitized[..2000];
    }

    private sealed class ClaimedRow
    {
        public Guid EventId { get; init; }
        public string EventType { get; init; } = null!;
        public int SchemaVersion { get; init; }
        public DateTime OccurredAtUtc { get; init; }
        public string Source { get; init; } = null!;
        public string Subject { get; init; } = null!;
        public Guid? ActorUserId { get; init; }
        public Guid CorrelationId { get; init; }
        public Guid? CausationId { get; init; }
        public string PayloadJson { get; init; } = null!;
        public DateTime CreatedAtUtc { get; init; }
        public string ConsumerCode { get; init; } = null!;
        public int AttemptCount { get; init; }
    }

    private sealed class HealthRow
    {
        public long PendingCount { get; init; }
        public DateTime? OldestOccurredAtUtc { get; init; }
        public long FailedCount { get; init; }
    }
}
