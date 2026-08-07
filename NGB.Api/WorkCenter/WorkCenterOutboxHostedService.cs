using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.Application.Abstractions.Services;
using NGB.Runtime.WorkCenter;

namespace NGB.Api.WorkCenter;

/// <summary>
/// API-host adapter that owns the polling lifetime of the Work Center projection processor.
/// The processor itself remains a Runtime application service and contains no hosting policy.
/// </summary>
internal sealed class WorkCenterOutboxHostedService(
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    IOptions<NgbWorkCenterOptions> options,
    ILogger<WorkCenterOutboxHostedService> logger)
    : BackgroundService
{
    private DateTimeOffset _nextMaintenanceAtUtc = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval);

        try
        {
            do
            {
                await DrainAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
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
            while (await processor.ProcessBatchAsync(options.Value.ProjectionBatchSize, stoppingToken) > 0)
            {
                // Drain ready work in bounded batches before yielding to the timer.
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
