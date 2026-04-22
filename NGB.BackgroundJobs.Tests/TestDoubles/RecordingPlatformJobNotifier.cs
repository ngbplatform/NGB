using NGB.BackgroundJobs.Contracts;

namespace NGB.BackgroundJobs.Tests.TestDoubles;

public sealed class RecordingPlatformJobNotifier : IPlatformJobNotifier
{
    private readonly bool _throwOnNotify;

    public RecordingPlatformJobNotifier(bool throwOnNotify = false)
    {
        _throwOnNotify = throwOnNotify;
    }

    public List<PlatformJobRunResult> Results { get; } = new();

    public Task NotifyAsync(PlatformJobRunResult result, CancellationToken cancellationToken)
    {
        Results.Add(result);

        if (_throwOnNotify)
            throw new NotSupportedException("notify failed");

        return Task.CompletedTask;
    }
}
