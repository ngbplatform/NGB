using NGB.Accounting.PostingState;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Idempotency;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.PostgreSql.ReferenceRegisters;

public sealed class PostgresReferenceRegisterIndependentWriteStateRepository(IUnitOfWork uow)
    : IReferenceRegisterIndependentWriteStateRepository
{
    private static readonly TimeSpan InProgressTimeout = PostgresIdempotencyLog.DefaultInProgressTimeout;

    public Task<PostingStateBeginResult> TryBeginAsync(
        Guid registerId,
        Guid commandId,
        ReferenceRegisterIndependentWriteOperation operation,
        DateTime startedAtUtc,
        CancellationToken ct = default)
        => PostgresIdempotencyLog.TryBeginAsync(
            uow,
            table: "reference_register_independent_write_state",
            historyTable: "reference_register_independent_write_log_history",
            keys:
            [
                new PostgresIdempotencyLog.Key("register_id", "RegisterId", registerId),
                new PostgresIdempotencyLog.Key("command_id", "CommandId", commandId),
                new PostgresIdempotencyLog.Key("operation", "Operation", (short)operation)
            ],
            startedAtUtc: startedAtUtc,
            inProgressTimeout: InProgressTimeout,
            notFoundMessage: () => $"Reference register independent write state row not found. registerId={registerId}, commandId={commandId}, operation={operation}",
            ct: ct);

    public Task MarkCompletedAsync(
        Guid registerId,
        Guid commandId,
        ReferenceRegisterIndependentWriteOperation operation,
        DateTime completedAtUtc,
        CancellationToken ct = default)
        => PostgresIdempotencyLog.MarkCompletedAsync(
            uow,
            table: "reference_register_independent_write_state",
            historyTable: "reference_register_independent_write_log_history",
            keys:
            [
                new PostgresIdempotencyLog.Key("register_id", "RegisterId", registerId),
                new PostgresIdempotencyLog.Key("command_id", "CommandId", commandId),
                new PostgresIdempotencyLog.Key("operation", "Operation", (short)operation)
            ],
            completedAtUtc: completedAtUtc,
            multiRowMessage: () => $"Failed to mark reference register independent write state completed. registerId={registerId}, commandId={commandId}, operation={operation}",
            context: new Dictionary<string, object?>
            {
                ["registerId"] = registerId,
                ["commandId"] = commandId,
                ["operation"] = operation.ToString()
            },
            ct: ct);
}
