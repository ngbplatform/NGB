namespace NGB.BackgroundJobs.Contracts;

/// <summary>
/// Provides a read-only health report about background jobs:
/// - desired schedules (from <see cref="IJobScheduleProvider"/>)
/// - actual recurring job state (from Hangfire storage)
///
/// The vertical application may expose this report via an HTTP endpoint.
/// </summary>
public interface IBackgroundJobsHealthReporter
{
    Task<BackgroundJobsHealthReport> GetReportAsync(CancellationToken cancellationToken);
}
