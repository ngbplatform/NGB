using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.Attachments;

namespace NGB.Api.Attachments;

internal sealed class AttachmentUploadExpirationHostedService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<AttachmentUploadExpirationHostedService> logger)
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
                    var expiration = scope.ServiceProvider.GetRequiredService<IAttachmentUploadExpirationService>();
                    await expiration.ExpirePendingAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError("Attachment upload expiration failed with {ErrorType}; retrying on next interval.", ex.GetType().Name);
                }

                await Task.Delay(TimeSpan.FromSeconds(30), clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
