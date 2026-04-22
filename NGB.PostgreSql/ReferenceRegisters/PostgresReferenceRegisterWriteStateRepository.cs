using Dapper;
using NGB.Accounting.PostingState;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Idempotency;
using NGB.PostgreSql.UnitOfWork;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.PostgreSql.ReferenceRegisters;

public sealed class PostgresReferenceRegisterWriteStateRepository(IUnitOfWork uow) : IReferenceRegisterWriteStateRepository
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
