using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;

namespace NGB.BackgroundJobs.Infrastructure;

/// <summary>
/// Registers recurring jobs from <see cref="PlatformJobCatalog"/> on startup.
/// </summary>
public sealed class PlatformHangfireRecurringJobsHostedService(
    IRecurringJobManager recurringJobManager,
    IJobScheduleProvider scheduleProvider,
    IBackgroundJobCatalog catalog,
    ILogger<PlatformHangfireRecurringJobsHostedService> logger)
    : IHostedService
{
    public PlatformHangfireRecurringJobsHostedService(
        IRecurringJobManager recurringJobManager,
        IJobScheduleProvider scheduleProvider,
        ILogger<PlatformHangfireRecurringJobsHostedService> logger)
        : this(
            recurringJobManager,
            scheduleProvider,
            BackgroundJobCatalog.FromJobIds(PlatformJobCatalog.All),
            logger)
    {
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var jobId in catalog.All)
        {
            var schedule = scheduleProvider.GetSchedule(jobId);
            if (schedule is null || !schedule.Enabled)
            {
                recurringJobManager.RemoveIfExists(jobId);
                logger.LogInformation("NGB.BackgroundJobs: recurring JobId={JobId} is disabled/unscheduled.", jobId);
                continue;
            }

            var timeZone = ResolveTimeZone(schedule.TimeZoneId);

            recurringJobManager.AddOrUpdate<PlatformHangfireJobRunner>(
                recurringJobId: jobId,
                methodCall: runner => runner.RunAsync(jobId, JobCancellationToken.Null),
                cronExpression: schedule.Cron,
                options: new RecurringJobOptions
                {
                    TimeZone = timeZone
                });

            logger.LogInformation(
                "NGB.BackgroundJobs: scheduled recurring JobId={JobId} cron={Cron} tz={TimeZoneId}.",
                jobId,
                schedule.Cron,
                schedule.TimeZoneId);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        if (string.Equals(timeZoneId, "UTC", StringComparison.OrdinalIgnoreCase))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }
}
