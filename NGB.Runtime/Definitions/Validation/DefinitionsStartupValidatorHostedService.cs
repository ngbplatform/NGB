using Microsoft.Extensions.Hosting;

namespace NGB.Runtime.Definitions.Validation;

/// <summary>
/// Ensures Definitions are validated when the host starts, so configuration bugs fail fast.
/// </summary>
internal sealed class DefinitionsStartupValidatorHostedService(IDefinitionsValidationService validator)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        validator.ValidateOrThrow();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
