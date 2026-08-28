using Dapper;
using NGB.Accounting.PostingState;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Idempotency;
using NGB.PostgreSql.UnitOfWork;
using NGB.ReferenceRegisters.Contracts;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.ReferenceRegisters;

public sealed class PostgresReferenceRegisterWriteStateRepository(IUnitOfWork uow)
    : IReferenceRegisterWriteStateRepository, IReferenceRegisterWriteStateBatchRepository
{
    private static readonly TimeSpan InProgressTimeout = PostgresIdempotencyLog.DefaultInProgressTimeout;

    public Task<PostingStateBeginResult> TryBeginAsync(
        Guid registerId,
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        DateTime startedAtUtc,
        CancellationToken ct = default)
        => PostgresIdempotencyLog.TryBeginAsync(
            uow,
            table: "reference_register_write_state",
            historyTable: "reference_register_write_log_history",
            keys:
            [
                new PostgresIdempotencyLog.Key("register_id", "RegisterId", registerId),
                new PostgresIdempotencyLog.Key("document_id", "DocumentId", documentId),
                new PostgresIdempotencyLog.Key("operation", "Operation", (short)operation)
            ],
            startedAtUtc: startedAtUtc,
            inProgressTimeout: InProgressTimeout,
            notFoundMessage: () => $"Reference register write state row not found. registerId={registerId}, documentId={documentId}, operation={operation}",
            ct: ct);

    public Task MarkCompletedAsync(
        Guid registerId,
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        DateTime completedAtUtc,
        CancellationToken ct = default)
        => PostgresIdempotencyLog.MarkCompletedAsync(
            uow,
            table: "reference_register_write_state",
            historyTable: "reference_register_write_log_history",
            keys:
            [
                new PostgresIdempotencyLog.Key("register_id", "RegisterId", registerId),
                new PostgresIdempotencyLog.Key("document_id", "DocumentId", documentId),
                new PostgresIdempotencyLog.Key("operation", "Operation", (short)operation)
            ],
            completedAtUtc: completedAtUtc,
            multiRowMessage: () => $"Failed to mark reference register write state completed. registerId={registerId}, documentId={documentId}, operation={operation}",
            context: new Dictionary<string, object?>
            {
                ["registerId"] = registerId,
                ["documentId"] = documentId,
                ["operation"] = operation.ToString()
            },
            ct: ct);

    public async Task<IReadOnlyDictionary<Guid, PostingStateBeginResult>> TryBeginManyAsync(
        IReadOnlyCollection<Guid> registerIds,
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        DateTime startedAtUtc,
        CancellationToken ct = default)
    {
        if (registerIds is null)
            throw new NgbArgumentRequiredException(nameof(registerIds));

        startedAtUtc.EnsureUtc(nameof(startedAtUtc));

        var ids = registerIds.Distinct().OrderBy(static id => id).ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, PostingStateBeginResult>();

        await uow.EnsureOpenForTransactionAsync(ct);

        var attemptIds = ids.Select(static _ => Guid.CreateVersion7()).ToArray();
        var startedHistoryIds = ids.Select(static _ => Guid.CreateVersion7()).ToArray();
        var supersededHistoryIds = ids.Select(static _ => Guid.CreateVersion7()).ToArray();

        const string sql = """
WITH input AS (
    SELECT *
    FROM unnest(
        @RegisterIds::uuid[],
        @AttemptIds::uuid[],
        @StartedHistoryIds::uuid[],
        @SupersededHistoryIds::uuid[]
    ) AS input(register_id, attempt_id, started_history_id, superseded_history_id)
),
previous AS MATERIALIZED (
    SELECT state.register_id, state.attempt_id
    FROM reference_register_write_state state
    JOIN input ON input.register_id = state.register_id
    WHERE state.document_id = @DocumentId
      AND state.operation = @Operation
),
upserted AS (
    INSERT INTO reference_register_write_state (
        register_id, document_id, operation, attempt_id, started_at_utc, completed_at_utc
    )
    SELECT register_id, @DocumentId, @Operation, attempt_id, @StartedAtUtc, NULL
    FROM input
    ON CONFLICT (register_id, document_id, operation) DO UPDATE
    SET attempt_id = EXCLUDED.attempt_id,
        started_at_utc = EXCLUDED.started_at_utc,
        completed_at_utc = NULL
    WHERE reference_register_write_state.completed_at_utc IS NULL
      AND reference_register_write_state.started_at_utc < @CutoffUtc
    RETURNING register_id, attempt_id
),
superseded_events AS (
    INSERT INTO reference_register_write_log_history (
        history_id, attempt_id, register_id, document_id, operation, event_kind, occurred_at_utc
    )
    SELECT
        input.superseded_history_id,
        previous.attempt_id,
        upserted.register_id,
        @DocumentId,
        @Operation,
        3,
        @StartedAtUtc
    FROM upserted
    JOIN input USING (register_id)
    JOIN previous USING (register_id)
    WHERE previous.attempt_id IS NOT NULL
    RETURNING 1
),
started_events AS (
    INSERT INTO reference_register_write_log_history (
        history_id, attempt_id, register_id, document_id, operation, event_kind, occurred_at_utc
    )
    SELECT
        input.started_history_id,
        upserted.attempt_id,
        upserted.register_id,
        @DocumentId,
        @Operation,
        1,
        @StartedAtUtc
    FROM upserted
    JOIN input USING (register_id)
    RETURNING 1
)
SELECT COUNT(*)::integer
FROM upserted;

SELECT
    input.register_id AS "RegisterId",
    CASE
        WHEN state.completed_at_utc IS NOT NULL THEN 2::smallint
        WHEN state.attempt_id = input.attempt_id THEN 1::smallint
        ELSE 3::smallint
    END AS "Result"
FROM unnest(@RegisterIds::uuid[], @AttemptIds::uuid[]) AS input(register_id, attempt_id)
JOIN reference_register_write_state state
  ON state.register_id = input.register_id
 AND state.document_id = @DocumentId
 AND state.operation = @Operation
ORDER BY input.register_id;
""";

        var command = new CommandDefinition(
            sql,
            new
            {
                RegisterIds = ids,
                AttemptIds = attemptIds,
                StartedHistoryIds = startedHistoryIds,
                SupersededHistoryIds = supersededHistoryIds,
                DocumentId = documentId,
                Operation = (short)operation,
                StartedAtUtc = startedAtUtc,
                CutoffUtc = startedAtUtc - InProgressTimeout
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await using var grid = await uow.Connection.QueryMultipleAsync(command);
        _ = await grid.ReadSingleAsync<int>();
        var rows = (await grid.ReadAsync<BatchBeginRow>()).ToArray();

        if (rows.Length != ids.Length)
            throw new NgbInvariantViolationException($"Reference register batch begin returned {rows.Length} states for {ids.Length} registers.");

        return rows.ToDictionary(
            static row => row.RegisterId,
            static row => (PostingStateBeginResult)row.Result);
    }

    public async Task MarkCompletedManyAsync(
        IReadOnlyCollection<Guid> registerIds,
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        DateTime completedAtUtc,
        CancellationToken ct = default)
    {
        if (registerIds is null)
            throw new NgbArgumentRequiredException(nameof(registerIds));

        completedAtUtc.EnsureUtc(nameof(completedAtUtc));

        var ids = registerIds.Distinct().OrderBy(static id => id).ToArray();
        if (ids.Length == 0)
            return;

        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
WITH input AS (
    SELECT *
    FROM unnest(@RegisterIds::uuid[], @HistoryIds::uuid[]) AS input(register_id, history_id)
),
updated AS (
    UPDATE reference_register_write_state state
    SET completed_at_utc = GREATEST(@CompletedAtUtc, state.started_at_utc)
    FROM input
    WHERE state.register_id = input.register_id
      AND state.document_id = @DocumentId
      AND state.operation = @Operation
      AND state.completed_at_utc IS NULL
    RETURNING state.register_id, state.attempt_id, state.completed_at_utc
),
completed_events AS (
    INSERT INTO reference_register_write_log_history (
        history_id, attempt_id, register_id, document_id, operation, event_kind, occurred_at_utc
    )
    SELECT
        input.history_id,
        updated.attempt_id,
        updated.register_id,
        @DocumentId,
        @Operation,
        2,
        updated.completed_at_utc
    FROM updated
    JOIN input USING (register_id)
    WHERE updated.attempt_id IS NOT NULL
    RETURNING 1
)
SELECT COUNT(*)::integer
FROM updated;
""";

        await uow.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new
            {
                RegisterIds = ids,
                HistoryIds = ids.Select(static _ => Guid.CreateVersion7()).ToArray(),
                DocumentId = documentId,
                Operation = (short)operation,
                CompletedAtUtc = completedAtUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    private sealed record BatchBeginRow(Guid RegisterId, short Result);

    public async Task<IReadOnlyList<Guid>> GetRegisterIdsByDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           SELECT DISTINCT register_id
                           FROM reference_register_write_state
                           WHERE document_id = @DocumentId
                             AND completed_at_utc IS NOT NULL
                             AND operation IN (@Post, @Repost)
                           ORDER BY register_id;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                DocumentId = documentId,
                Post = (short)ReferenceRegisterWriteOperation.Post,
                Repost = (short)ReferenceRegisterWriteOperation.Repost
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync<Guid>(cmd);
        return rows.ToArray();
    }

    public async Task ClearCompletedStateByDocumentAsync(
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        CancellationToken ct = default)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           DELETE FROM reference_register_write_state
                           WHERE document_id = @DocumentId
                             AND operation = @Operation
                             AND completed_at_utc IS NOT NULL;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { DocumentId = documentId, Operation = (short)operation },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }
}
