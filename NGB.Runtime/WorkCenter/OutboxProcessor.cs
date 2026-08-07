using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NGB.Application.Abstractions.IntegrationEvents;
using NGB.Application.Abstractions.Services;
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
    IWorkCenterChangeTracker changes,
    WorkCenterPreferenceRecipientResolver recipientResolver,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger)
    : IOutboxProcessor
{
    private const string ConsumerCode = "work-center";
    private const int MaximumAttempts = 8;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly IReadOnlyList<IDocumentActionCompletedWorkCenterPolicy> _policies = policies.ToArray();

    public async Task<int> ProcessBatchAsync(int batchSize, CancellationToken ct)
    {
        changes.Reset();
        var items = await uow.ExecuteInUowTransactionAsync(
            innerCt => outbox.ClaimBatchAsync(
                ConsumerCode,
                // Keep leases bounded: policies run sequentially to preserve event ordering,
                // so a very large claimed tail would otherwise age while waiting in memory.
                Math.Clamp(batchSize, 1, 25),
                timeProvider.GetUtcNowDateTime(),
                innerCt),
            ct);

        var changedUsers = new HashSet<Guid>();
        foreach (var item in items)
        {
            changes.Reset();
            recipientResolver.Reset();
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
                        foreach (var policy in _policies)
                        {
                            var policyStarted = Stopwatch.GetTimestamp();
                            await policy.HandleAsync(completed, innerCt);
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
                changedUsers.UnionWith(changes.Drain());
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                changes.Reset();
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
