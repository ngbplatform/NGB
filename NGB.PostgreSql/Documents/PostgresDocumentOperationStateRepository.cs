using Dapper;
using NGB.Accounting.PostingState;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Idempotency;
using NGB.PostgreSql.UnitOfWork;

namespace NGB.PostgreSql.Documents;

/// <summary>
/// PostgreSQL implementation of <see cref="IDocumentOperationStateRepository"/>.
///
/// State table:   platform_document_operation_state
/// History table: platform_document_operation_history
/// Key:           (document_id, operation)
///
/// The state row is ephemeral technical dedupe/in-flight state.
/// Immutable history is written separately by the shared idempotency helper.
/// </summary>
public sealed class PostgresDocumentOperationStateRepository(IUnitOfWork uow) : IDocumentOperationStateRepository
{
    private static readonly TimeSpan InProgressTimeout = PostgresIdempotencyLog.DefaultInProgressTimeout;

    public Task<PostingStateBeginResult> TryBeginAsync(
        Guid documentId,
        PostingOperation operation,
        DateTime startedAtUtc,
        CancellationToken ct = default)
        => PostgresIdempotencyLog.TryBeginAsync(
            uow,
            table: "platform_document_operation_state",
            historyTable: "platform_document_operation_history",
            keys:
            [
                new PostgresIdempotencyLog.Key("document_id", "DocumentId", documentId),
                new PostgresIdempotencyLog.Key("operation", "Operation", (short)operation)
            ],
            startedAtUtc: startedAtUtc,
            inProgressTimeout: InProgressTimeout,
            notFoundMessage: () => $"Document operation state row not found. documentId={documentId}, operation={operation}",
            ct: ct);

    public Task MarkCompletedAsync(
        Guid documentId,
        PostingOperation operation,
        DateTime completedAtUtc,
        CancellationToken ct = default)
        => PostgresIdempotencyLog.MarkCompletedAsync(
            uow,
            table: "platform_document_operation_state",
            historyTable: "platform_document_operation_history",
            keys:
            [
                new PostgresIdempotencyLog.Key("document_id", "DocumentId", documentId),
                new PostgresIdempotencyLog.Key("operation", "Operation", (short)operation)
            ],
            completedAtUtc: completedAtUtc,
            multiRowMessage: () => "Document operation state update affected more than one row.",
            context: new Dictionary<string, object?>
            {
                ["documentId"] = documentId,
                ["operation"] = operation.ToString()
            },
            ct: ct);

    public async Task ClearCompletedStateAsync(
        Guid documentId,
        PostingOperation operation,
        CancellationToken ct = default)
    {
        await ClearStateAsync(documentId, operation, completed: true, ct);
    }

    public async Task ClearInProgressStateAsync(
        Guid documentId,
        PostingOperation operation,
        CancellationToken ct = default)
    {
        await ClearStateAsync(documentId, operation, completed: false, ct);
    }

    private async Task ClearStateAsync(
        Guid documentId,
        PostingOperation operation,
        bool completed,
        CancellationToken ct)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        var sql = completed
            ? """
              DELETE FROM platform_document_operation_state
              WHERE document_id = @DocumentId
                AND operation = @Operation
                AND completed_at_utc IS NOT NULL;
              """
            : """
              DELETE FROM platform_document_operation_state
              WHERE document_id = @DocumentId
                AND operation = @Operation
                AND completed_at_utc IS NULL;
              """;

        var cmd = new CommandDefinition(
            sql,
            new { DocumentId = documentId, Operation = (short)operation },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }
}
