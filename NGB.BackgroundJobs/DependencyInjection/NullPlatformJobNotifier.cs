using NGB.BackgroundJobs.Contracts;

namespace NGB.BackgroundJobs.DependencyInjection;

public sealed class NullPlatformJobNotifier : IPlatformJobNotifier
{
    public Task NotifyAsync(PlatformJobRunResult result, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
