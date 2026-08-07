namespace NGB.Persistence.Outbox;

public enum OutboxConsumerStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    DeadLetter = 5
}

public sealed class OutboxConsumerWorkItem(
    OutboxEventEnvelope @event,
    string consumerCode,
    int attemptCount)
{
    public OutboxEventEnvelope Event { get; } = @event;
    public string ConsumerCode { get; } = consumerCode;
    public int AttemptCount { get; } = attemptCount;
}

public interface IOutboxEventRepository
{
    Task AppendAsync(
        OutboxEventEnvelope outboxEvent,
        IReadOnlyList<string> consumerCodes,
        CancellationToken ct);

    Task<IReadOnlyList<OutboxConsumerWorkItem>> ClaimBatchAsync(
        string consumerCode,
        int batchSize,
        DateTime nowUtc,
        CancellationToken ct);

    Task MarkCompletedAsync(
        Guid eventId,
        string consumerCode,
        int attemptNumber,
        DateTime completedAtUtc,
        CancellationToken ct);

    Task MarkFailedAsync(
        Guid eventId,
        string consumerCode,
        int attemptNumber,
        DateTime completedAtUtc,
        DateTime? nextAttemptAtUtc,
        string sanitizedError,
        bool deadLetter,
        CancellationToken ct);

    Task<(long PendingCount, DateTime? OldestOccurredAtUtc, long FailedCount)> GetHealthAsync(
        string consumerCode,
        CancellationToken ct);
}
