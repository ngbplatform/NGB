using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.Runtime.Reporting.Runs;

namespace NGB.Runtime.Hosting;

internal sealed class ReportRunsHostedService(ReportRunProcessor processor, ILogger<ReportRunsHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cleanupAt = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow >= cleanupAt)
                {
                    await processor.CleanupAsync(stoppingToken);
                    cleanupAt = DateTime.UtcNow.AddMinutes(1);
                }

                if (await processor.ProcessNextAsync(stoppingToken))
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Report execution queue could not be processed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
