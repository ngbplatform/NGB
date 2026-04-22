using Dapper;
using NGB.Accounting.PostingState;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.PostgreSql.Idempotency;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// PostgreSQL implementation of <see cref="IOperationalRegisterWriteStateRepository"/>.
///
/// Table: operational_register_write_state
/// Key: (register_id, document_id, operation)
///
/// IMPORTANT:
/// - This repository MUST be used within an active transaction.
/// - The state row is committed atomically with operational register writes.
/// </summary>
public sealed class PostgresOperationalRegisterWriteStateRepository(IUnitOfWork uow)
    : IOperationalRegisterWriteStateRepository
{
    private static readonly TimeSpan InProgressTimeout = PostgresIdempotencyLog.DefaultInProgressTimeout;

    public Task<PostingStateBeginResult> TryBeginAsync(
        Guid registerId,
        Guid documentId,
        OperationalRegisterWriteOperation operation,
        DateTime startedAtUtc,
        CancellationToken ct = default)
        => PostgresIdempotencyLog.TryBeginAsync(
            uow,
            table: "operational_register_write_state",
            historyTable: "operational_register_write_log_history",
            keys:
            [
                new PostgresIdempotencyLog.Key("register_id", "RegisterId", registerId),
                new PostgresIdempotencyLog.Key("document_id", "DocumentId", documentId),
                new PostgresIdempotencyLog.Key("operation", "Operation", (short)operation)
            ],
            startedAtUtc: startedAtUtc,
            inProgressTimeout: InProgressTimeout,
            notFoundMessage: () => $"Operational register write state row not found. registerId={registerId}, documentId={documentId}, operation={operation}",
            ct: ct);

    public Task MarkCompletedAsync(
        Guid registerId,
        Guid documentId,
        OperationalRegisterWriteOperation operation,
        DateTime completedAtUtc,
        CancellationToken ct = default)
        => PostgresIdempotencyLog.MarkCompletedAsync(
            uow,
            table: "operational_register_write_state",
            historyTable: "operational_register_write_log_history",
            keys:
            [
                new PostgresIdempotencyLog.Key("register_id", "RegisterId", registerId),
                new PostgresIdempotencyLog.Key("document_id", "DocumentId", documentId),
                new PostgresIdempotencyLog.Key("operation", "Operation", (short)operation)
            ],
            completedAtUtc: completedAtUtc,
            multiRowMessage: () => $"Failed to mark operational register write state completed. registerId={registerId}, documentId={documentId}, operation={operation}",
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
                           FROM operational_register_write_state
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
                Post = (short)OperationalRegisterWriteOperation.Post,
                Repost = (short)OperationalRegisterWriteOperation.Repost
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync<Guid>(cmd);
        return rows.ToArray();
    }

    public async Task ClearCompletedStateByDocumentAsync(
        Guid documentId,
        OperationalRegisterWriteOperation operation,
        CancellationToken ct = default)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           DELETE FROM operational_register_write_state
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
