using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.IntegrationEvents;
using NGB.Persistence.Outbox;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;
using NGB.Runtime.Observability;
using NGB.Tools.Extensions;

namespace NGB.Runtime.WorkCenter;

internal sealed class OutboxProcessor(
    IUnitOfWork uow,
    IOutboxEventRepository outbox,
    IEnumerable<IDocumentActionCompletedWorkCenterPolicy> policies,
    IWorkCenterRealtimeNotifier realtime,
    WorkCenterPreferenceRecipientResolver recipientResolver,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger,
    IWorkCenterOutboxPartitionProcessorFactory? partitionProcessorFactory = null,
    IOptions<NgbWorkCenterOptions>? options = null)
    : IOutboxProcessor
{
    private const string ConsumerCode = "work-center";
    private readonly IReadOnlyList<IDocumentActionCompletedWorkCenterPolicy> _policies = policies.ToArray();

    public async Task<int> ProcessBatchAsync(int batchSize, CancellationToken ct)
    {
        var items = await uow.ExecuteInUowTransactionAsync(
            innerCt => outbox.ClaimBatchAsync(
                ConsumerCode,
                // Keep leases bounded: each subject remains sequential, so a very large
                // claimed partition could otherwise age while waiting in memory.
                Math.Clamp(batchSize, 1, 25),
                timeProvider.GetUtcNowDateTime(),
                innerCt),
            ct);

        IReadOnlyCollection<Guid> changedUsers;
        var parallelism = options?.Value.ProjectionParallelism ?? 1;
        var partitions = items
            .GroupBy(static item => item.Event.Subject, StringComparer.Ordinal)
            .Select(static group => (IReadOnlyList<OutboxConsumerWorkItem>)group.ToArray())
            .ToArray();

        if (partitionProcessorFactory is not null && parallelism > 1 && partitions.Length > 1)
        {
            var concurrentChangedUsers = new ConcurrentDictionary<Guid, byte>();
            await Parallel.ForEachAsync(
                partitions,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(parallelism, partitions.Length),
                    CancellationToken = ct
                },
                async (partition, innerCt) =>
                {
                    var partitionChangedUsers = await partitionProcessorFactory.ProcessAsync(partition, innerCt);
                    foreach (var userId in partitionChangedUsers)
                        concurrentChangedUsers.TryAdd(userId, 0);
                });
            changedUsers = concurrentChangedUsers.Keys.ToArray();
        }
        else
        {
            changedUsers = await WorkCenterOutboxProjectionRunner.ProcessAsync(
                items,
                uow,
                outbox,
                _policies,
                recipientResolver,
                timeProvider,
                logger,
                ct);
        }

        if (changedUsers.Count > 0)
        {
            try
            {
                await realtime.NotifyUsersChangedAsync(
                    timeProvider.GetUtcNowDateTime().Ticks,
                    changedUsers,
                    ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Work Center realtime invalidation failed after projecting {EventCount} outbox events.",
                    items.Count);
            }
        }

        return items.Count;
    }
}

internal interface IWorkCenterOutboxPartitionProcessorFactory
{
    Task<IReadOnlyCollection<Guid>> ProcessAsync(
        IReadOnlyList<OutboxConsumerWorkItem> items,
        CancellationToken ct);
}

internal sealed class WorkCenterOutboxPartitionProcessorFactory(IServiceScopeFactory scopes)
    : IWorkCenterOutboxPartitionProcessorFactory
{
    public async Task<IReadOnlyCollection<Guid>> ProcessAsync(
        IReadOnlyList<OutboxConsumerWorkItem> items,
        CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<WorkCenterOutboxPartitionProcessor>();
        return await processor.ProcessAsync(items, ct);
    }
}

internal sealed class WorkCenterOutboxPartitionProcessor(
    IUnitOfWork uow,
    IOutboxEventRepository outbox,
    IEnumerable<IDocumentActionCompletedWorkCenterPolicy> policies,
    WorkCenterPreferenceRecipientResolver recipientResolver,
    TimeProvider timeProvider,
    ILogger<WorkCenterOutboxPartitionProcessor> logger)
{
    private readonly IReadOnlyList<IDocumentActionCompletedWorkCenterPolicy> _policies = policies.ToArray();

    public Task<IReadOnlyCollection<Guid>> ProcessAsync(
        IReadOnlyList<OutboxConsumerWorkItem> items,
        CancellationToken ct)
        => WorkCenterOutboxProjectionRunner.ProcessAsync(
            items,
            uow,
            outbox,
            _policies,
            recipientResolver,
            timeProvider,
            logger,
            ct);
}

internal static class WorkCenterOutboxProjectionRunner
{
    private const string ConsumerCode = "work-center";
    private const int MaximumAttempts = 8;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<IReadOnlyCollection<Guid>> ProcessAsync(
        IReadOnlyList<OutboxConsumerWorkItem> items,
        IUnitOfWork uow,
        IOutboxEventRepository outbox,
        IReadOnlyList<IDocumentActionCompletedWorkCenterPolicy> policies,
        WorkCenterPreferenceRecipientResolver recipientResolver,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken ct)
    {
        // Recipient metadata is stable for one subject partition and is deliberately discarded
        // before it is reused. This keeps the cache scoped, bounded and concurrency-safe.
        recipientResolver.Reset();

        var changedUsers = new HashSet<Guid>();
        foreach (var item in items)
        {
            var eventChangedUsers = new HashSet<Guid>();
            using var activity = NgbFeatureTelemetry.Activities.StartActivity("work_center.outbox.project", ActivityKind.Consumer);
            activity?.SetTag("messaging.system", "postgresql");
            activity?.SetTag("messaging.destination.name", ConsumerCode);
            activity?.SetTag("messaging.message.type", item.Event.EventType);

            try
            {
                await uow.ExecuteInUowTransactionAsync(async innerCt =>
                {
                    if (string.Equals(
                            item.Event.EventType,
                            DocumentActionCompletedV1.EventType,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        var completed = DeserializeDocumentActionCompleted(item.Event);
                        foreach (var policy in policies)
                        {
                            var policyStarted = Stopwatch.GetTimestamp();
                            var policyChangedUsers = await policy.HandleAsync(completed, innerCt);
                            eventChangedUsers.UnionWith(policyChangedUsers);
                            NgbFeatureTelemetry.WorkCenterPolicyDuration.Record(
                                Stopwatch.GetElapsedTime(policyStarted).TotalMilliseconds,
                                new KeyValuePair<string, object?>("event.type", item.Event.EventType),
                                new KeyValuePair<string, object?>("policy.type", policy.GetType().Name));
                        }
                    }

                    await outbox.MarkCompletedAsync(
                        item.Event.EventId,
                        ConsumerCode,
                        item.AttemptCount,
                        timeProvider.GetUtcNowDateTime(),
                        innerCt);
                }, ct);

                NgbFeatureTelemetry.OutboxProcessed.Add(
                    1,
                    new KeyValuePair<string, object?>("consumer", ConsumerCode),
                    new KeyValuePair<string, object?>("event.type", item.Event.EventType));
                activity?.SetStatus(ActivityStatusCode.Ok);
                changedUsers.UnionWith(eventChangedUsers);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                NgbFeatureTelemetry.OutboxFailures.Add(
                    1,
                    new KeyValuePair<string, object?>("consumer", ConsumerCode),
                    new KeyValuePair<string, object?>("event.type", item.Event.EventType));

                activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
                logger.LogError(
                    ex,
                    "Work Center outbox policy failed for event {EventId} on attempt {Attempt}.",
                    item.Event.EventId,
                    item.AttemptCount);

                var deadLetter = item.AttemptCount >= MaximumAttempts;
                var nextAttempt = deadLetter
                    ? (DateTime?)null
                    : timeProvider.GetUtcNowDateTime().Add(Backoff(item.Event.EventId, item.AttemptCount));

                await uow.ExecuteInUowTransactionAsync(
                    innerCt => outbox.MarkFailedAsync(
                        item.Event.EventId,
                        ConsumerCode,
                        item.AttemptCount,
                        timeProvider.GetUtcNowDateTime(),
                        nextAttempt,
                        $"{ex.GetType().Name}: {ex.Message}",
                        deadLetter,
                        innerCt),
                    ct);
            }
        }

        return changedUsers;
    }

    private static TimeSpan Backoff(Guid eventId, int attempt)
    {
        var seconds = Math.Min(900, Math.Pow(2, Math.Clamp(attempt, 1, 10)));
        var bytes = eventId.ToByteArray();
        var jitter = BitConverter.ToUInt16(bytes, 0) / (double)ushort.MaxValue;
        return TimeSpan.FromSeconds(seconds * (0.8 + jitter * 0.4));
    }

    private static DocumentActionCompletedV1 DeserializeDocumentActionCompleted(OutboxEventEnvelope envelope)
    {
        if (envelope.SchemaVersion != DocumentActionCompletedV1.SchemaVersion)
            throw new JsonException($"Unsupported '{DocumentActionCompletedV1.EventType}' schema version '{envelope.SchemaVersion}'.");

        var completed = JsonSerializer.Deserialize<DocumentActionCompletedV1>(envelope.PayloadJson, Json)
            ?? throw new JsonException("Document action completed payload is empty.");

        if (completed.EventId != envelope.EventId
            || completed.CorrelationId != envelope.CorrelationId
            || !string.Equals(completed.Type, envelope.EventType, StringComparison.Ordinal)
            || completed.PayloadSchemaVersion != envelope.SchemaVersion)
        {
            throw new JsonException("Document action completed payload does not match its outbox envelope.");
        }

        return completed;
    }
}

internal sealed class NullWorkCenterRealtimeNotifier : IWorkCenterRealtimeNotifier
{
    public Task NotifyUsersChangedAsync(long version, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
        => Task.CompletedTask;
}
