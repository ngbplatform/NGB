namespace NGB.BackgroundJobs.Contracts;

/// <summary>
/// Best-effort notification hook for platform background job runs.
///
/// The platform does not send emails/Slack/etc. The vertical application is expected to provide
/// an implementation (e.g. email) that reacts to <see cref="PlatformJobRunResult.HasProblems"/>.
/// </summary>
public interface IPlatformJobNotifier
{
    Task NotifyAsync(PlatformJobRunResult result, CancellationToken cancellationToken);
}
