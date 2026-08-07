using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    IEnumerable<IWorkCenterEventPolicy> policies,
    IWorkCenterRealtimeNotifier realtime,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger)
    : IOutboxProcessor
{
    private const string ConsumerCode = "work-center";
    private const int MaximumAttempts = 8;
    private readonly IReadOnlyList<IWorkCenterEventPolicy> _policies = policies.ToArray();

    public async Task<int> ProcessBatchAsync(int batchSize, CancellationToken ct)
    {
        var items = await uow.ExecuteInUowTransactionAsync(
            innerCt => outbox.ClaimBatchAsync(
                ConsumerCode,
                Math.Clamp(batchSize, 1, 500),
                timeProvider.GetUtcNowDateTime(),
                innerCt),
            ct);

        foreach (var item in items)
        {
            using var activity = NgbFeatureTelemetry.Activities.StartActivity("work_center.outbox.project", ActivityKind.Consumer);
            activity?.SetTag("messaging.system", "postgresql");
            activity?.SetTag("messaging.destination.name", ConsumerCode);
            activity?.SetTag("messaging.message.type", item.Event.EventType);

            try
            {
                await uow.ExecuteInUowTransactionAsync(async innerCt =>
                {
                    foreach (var policy in _policies.Where(
                         x => string.Equals(
                             x.EventType, item.Event.EventType, StringComparison.OrdinalIgnoreCase)))
                    {
                        var policyStarted = Stopwatch.GetTimestamp();
                        await policy.HandleAsync(new WorkCenterEventContext(item.Event), innerCt);
                        NgbFeatureTelemetry.WorkCenterPolicyDuration.Record(
                            Stopwatch.GetElapsedTime(policyStarted).TotalMilliseconds,
                            new KeyValuePair<string, object?>("event.type", item.Event.EventType),
                            new KeyValuePair<string, object?>("policy.type", policy.GetType().Name));
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

                try
                {
                    await realtime.NotifyChangedAsync(timeProvider.GetUtcNowDateTime().Ticks, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Work Center realtime invalidation failed after event {EventId} was projected.",
                        item.Event.EventId);
                }
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

        return items.Count;
    }

    private static TimeSpan Backoff(Guid eventId, int attempt)
    {
        var seconds = Math.Min(900, Math.Pow(2, Math.Clamp(attempt, 1, 10)));
        var bytes = eventId.ToByteArray();
        var jitter = BitConverter.ToUInt16(bytes, 0) / (double)ushort.MaxValue;
        return TimeSpan.FromSeconds(seconds * (0.8 + jitter * 0.4));
    }
}

internal sealed class NullWorkCenterRealtimeNotifier : IWorkCenterRealtimeNotifier
{
    public Task NotifyChangedAsync(long version, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class WorkCenterOutboxHostedService(
    IServiceScopeFactory scopes,
    ILogger<WorkCenterOutboxHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        try
        {
            while (true)
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await using var scope = scopes.CreateAsyncScope();

                if (scope.ServiceProvider.GetService<IOutboxEventRepository>() is null)
                {
                    logger.LogDebug("Work Center outbox processor is disabled because no outbox repository is registered.");
                    return;
                }

                var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
                try
                {
                    while (await processor.ProcessBatchAsync(100, stoppingToken) > 0)
                    {
                        // Drain ready work in bounded batches before yielding to the timer.
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Work Center outbox polling failed; processing will retry on the next interval.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful host shutdown.
        }
    }
}
