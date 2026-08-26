using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.Application.Abstractions.Services;

namespace NGB.Api.WorkCenter;

/// <summary>
/// API-host adapter that owns the polling lifetime of the Work Center projection processor.
/// The processor itself remains a Runtime application service and contains no hosting policy.
/// </summary>
internal sealed class WorkCenterOutboxHostedService(
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    IOptions<NgbWorkCenterHostingOptions> options,
    ILogger<WorkCenterOutboxHostedService> logger)
    : BackgroundService
{
    private DateTimeOffset _nextMaintenanceAtUtc = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            do
            {
                await DrainAsync(stoppingToken);
                // Fixed-delay polling deliberately starts the interval after a
                // drain. A periodic timer can retain a tick that elapsed while a
                // busy drain was running and immediately start another drain,
                // defeating the per-poll batch bound and creating a hot loop.
                await Task.Delay(options.Value.PollInterval, timeProvider, stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful host shutdown.
        }
    }

    private async Task DrainAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();

            for (var batch = 0; batch < options.Value.MaximumProjectionBatchesPerPoll; batch++)
            {
                var processed = await processor.ProcessBatchAsync(options.Value.ProjectionBatchSize, stoppingToken);
                if (processed == 0)
                    break;
            }

            var now = timeProvider.GetUtcNow();
            if (now >= _nextMaintenanceAtUtc)
            {
                _nextMaintenanceAtUtc = now.Add(options.Value.MaintenanceInterval);
                var maintenance = scope.ServiceProvider.GetRequiredService<IWorkCenterMaintenanceService>();
                var pruned = await maintenance.PruneAsync(stoppingToken);

                if (pruned > 0)
                    logger.LogInformation("Pruned {PrunedCount} expired Work Center records.", pruned);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Work Center outbox polling failed; processing will retry on the next interval.");
        }
    }
}
