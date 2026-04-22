using Dapper;
using NGB.Accounting.PostingState;
using NGB.Persistence.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Idempotency;
using NGB.PostgreSql.UnitOfWork;

namespace NGB.PostgreSql.PostingState;

/// <summary>
/// PostgreSQL implementation of <see cref="IPostingStateRepository"/>.
///
/// Table: accounting_posting_state
/// Key: (document_id, operation)
///
/// IMPORTANT:
/// - This repository MUST be used within an active transaction.
/// - The state row is committed atomically with accounting writes.
/// </summary>
internal sealed class PostgresPostingStateRepository(IUnitOfWork uow) : IPostingStateRepository
{
    private static readonly TimeSpan InProgressTimeout = PostgresIdempotencyLog.DefaultInProgressTimeout;

    public Task<PostingStateBeginResult> TryBeginAsync(
        Guid documentId,
        PostingOperation operation,
        DateTime startedAtUtc,
        CancellationToken ct = default)
        => PostgresIdempotencyLog.TryBeginAsync(
            uow,
            table: "accounting_posting_state",
            historyTable: "accounting_posting_log_history",
            keys:
            [
                new PostgresIdempotencyLog.Key("document_id", "DocumentId", documentId),
                new PostgresIdempotencyLog.Key("operation", "Operation", (short)operation)
            ],
            startedAtUtc: startedAtUtc,
            inProgressTimeout: InProgressTimeout,
            notFoundMessage: () => $"Posting state row not found. documentId={documentId}, operation={operation}",
            ct: ct);

    public Task MarkCompletedAsync(
        Guid documentId,
        PostingOperation operation,
        DateTime completedAtUtc,
        CancellationToken ct = default)
        => PostgresIdempotencyLog.MarkCompletedAsync(
            uow,
            table: "accounting_posting_state",
            historyTable: "accounting_posting_log_history",
            keys:
            [
                new PostgresIdempotencyLog.Key("document_id", "DocumentId", documentId),
                new PostgresIdempotencyLog.Key("operation", "Operation", (short)operation)
            ],
            completedAtUtc: completedAtUtc,
            multiRowMessage: () => "Posting state update affected more than one row.",
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
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           DELETE FROM accounting_posting_state
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
