using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Runtime.Definitions.Validation;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Hosting;

public static class RuntimeHostingServiceCollectionExtensions
{
    /// <summary>
    /// Adds fail-fast validation of the composed NGB definitions to a generic host.
    /// Call this from a host composition root after choosing to host NGB Runtime.
    /// </summary>
    public static IServiceCollection AddNgbRuntimeStartupValidation(this IServiceCollection services)
    {
        if (services is null)
            throw new NgbArgumentRequiredException(nameof(services));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DefinitionsStartupValidatorHostedService>());

        return services;
    }
}

internal sealed class DefinitionsStartupValidatorHostedService(IDefinitionsValidationService validator)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => validator.ValidateOrThrowAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
