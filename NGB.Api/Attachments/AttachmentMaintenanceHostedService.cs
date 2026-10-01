using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.Attachments;

namespace NGB.Api.Attachments;

internal sealed class AttachmentMaintenanceHostedService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<AttachmentMaintenanceHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var maintenance = scope.ServiceProvider.GetRequiredService<IAttachmentMaintenance>();
                    await maintenance.ExpirePendingAsync(stoppingToken);
                    await maintenance.ProcessCleanupAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError("Attachment maintenance failed with {ErrorType}; retrying on next interval.", ex.GetType().Name);
                }

                await Task.Delay(TimeSpan.FromSeconds(30), clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
