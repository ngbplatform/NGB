using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Attachments;
using NGB.Tools.Exceptions;

namespace NGB.Api.Attachments;

internal sealed class AttachmentStorageStartupValidator(IServiceScopeFactory scopes) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        if (scope.ServiceProvider.GetService<IAttachmentObjectStorage>() is null)
            throw new NgbConfigurationViolationException("Attachments is enabled. Register and configure an attachment storage provider.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
