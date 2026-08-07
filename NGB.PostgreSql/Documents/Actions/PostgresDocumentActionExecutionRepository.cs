using Dapper;
using System.Diagnostics.CodeAnalysis;
using NGB.Persistence.Documents.Actions;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Documents.Actions;

public sealed class PostgresDocumentActionExecutionRepository(IUnitOfWork uow)
    : IDocumentActionExecutionRepository
{
    public async Task<DocumentActionExecutionBeginResult> TryBeginAsync(
        string idempotencyKey,
        string requestFingerprint,
        Guid documentId,
        string documentType,
        string actionCode,
        DateTime startedAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new NgbArgumentRequiredException(nameof(idempotencyKey));

        if (idempotencyKey.Length > 200)
            throw new NgbArgumentInvalidException(nameof(idempotencyKey), "Idempotency key cannot exceed 200 characters.");

        if (requestFingerprint is null || requestFingerprint.Length != 64)
            throw new NgbArgumentInvalidException(nameof(requestFingerprint), "Request fingerprint must be a SHA-256 hex value.");

        startedAtUtc.EnsureUtc(nameof(startedAtUtc));
        await uow.EnsureOpenForTransactionAsync(ct);

        var executionId = Guid.CreateVersion7();
        const string insertSql = """
            INSERT INTO platform_document_action_executions (
                execution_id, idempotency_key, request_fingerprint,
                document_id, document_type, action_code, started_at_utc
            )
            VALUES (
                @ExecutionId, @IdempotencyKey, @RequestFingerprint,
                @DocumentId, @DocumentType, @ActionCode, @StartedAtUtc
            )
            ON CONFLICT (idempotency_key) DO NOTHING;
            """;

        var args = new
        {
            ExecutionId = executionId,
            IdempotencyKey = idempotencyKey.Trim(),
            RequestFingerprint = requestFingerprint,
            DocumentId = documentId,
            DocumentType = documentType,
            ActionCode = actionCode,
            StartedAtUtc = startedAtUtc
        };

        var inserted = await uow.Connection.ExecuteAsync(new CommandDefinition(insertSql, args, uow.Transaction, cancellationToken: ct));

        if (inserted == 1)
        {
            return new DocumentActionExecutionBeginResult(
                DocumentActionExecutionBeginStatus.Begun,
                executionId,
                ResultJson: null);
        }

        const string selectSql = """
            SELECT execution_id AS "ExecutionId",
                   request_fingerprint AS "RequestFingerprint",
                   completed_at_utc AS "CompletedAtUtc",
                   result_json::text AS "ResultJson"
            FROM platform_document_action_executions
            WHERE idempotency_key = @IdempotencyKey
            FOR UPDATE;
            """;

        var row = await uow.Connection.QuerySingleOrDefaultAsync<ExecutionRow>(
            new CommandDefinition(
                selectSql,
                new { IdempotencyKey = idempotencyKey.Trim() },
                uow.Transaction,
                cancellationToken: ct));

        row = RequireExecutionRow(row);

        if (!StringComparer.Ordinal.Equals(row.RequestFingerprint, requestFingerprint))
        {
            return new DocumentActionExecutionBeginResult(
                DocumentActionExecutionBeginStatus.Conflict,
                row.ExecutionId,
                ResultJson: null);
        }

        return row.CompletedAtUtc is not null && row.ResultJson is not null
            ? new DocumentActionExecutionBeginResult(
                DocumentActionExecutionBeginStatus.Completed,
                row.ExecutionId,
                row.ResultJson)
            : new DocumentActionExecutionBeginResult(
                DocumentActionExecutionBeginStatus.InProgress,
                row.ExecutionId,
                ResultJson: null);
    }

    [ExcludeFromCodeCoverage(Justification = "A row cannot disappear between ON CONFLICT and SELECT FOR UPDATE in the same PostgreSQL transaction; this guard detects database corruption or an invalid trigger.")]
    private static ExecutionRow RequireExecutionRow(ExecutionRow? row)
        => row ?? throw new NgbInvariantViolationException("Document action execution disappeared after idempotency-key conflict.");

    public async Task MarkCompletedAsync(
        Guid executionId,
        string resultJson,
        DateTime completedAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
            throw new NgbArgumentRequiredException(nameof(resultJson));

        completedAtUtc.EnsureUtc(nameof(completedAtUtc));
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
            UPDATE platform_document_action_executions
            SET completed_at_utc = GREATEST(@CompletedAtUtc, started_at_utc),
                result_json = CAST(@ResultJson AS jsonb)
            WHERE execution_id = @ExecutionId
              AND completed_at_utc IS NULL;
            """;

        var rows = await uow.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { ExecutionId = executionId, ResultJson = resultJson, CompletedAtUtc = completedAtUtc },
                uow.Transaction,
                cancellationToken: ct));

        if (rows != 1)
        {
            throw new NgbInvariantViolationException(
                "Document action execution could not be completed.",
                new Dictionary<string, object?> { ["executionId"] = executionId, ["rows"] = rows });
        }
    }

    private sealed class ExecutionRow
    {
        public Guid ExecutionId { get; init; }
        public string RequestFingerprint { get; init; } = null!;
        public DateTime? CompletedAtUtc { get; init; }
        public string? ResultJson { get; init; }
    }
}
