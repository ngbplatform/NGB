using Microsoft.Extensions.Logging;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;
using NGB.Tools.Extensions;

namespace NGB.BackgroundJobs.Observability;

internal sealed class BackgroundJobsHealthReporter(
    IJobScheduleProvider schedules,
    IRecurringJobStateReader recurring,
    IBackgroundJobCatalog catalog,
    ILogger<BackgroundJobsHealthReporter> logger,
    TimeProvider timeProvider)
    : IBackgroundJobsHealthReporter
{
    public BackgroundJobsHealthReporter(
        IJobScheduleProvider schedules,
        IRecurringJobStateReader recurring,
        ILogger<BackgroundJobsHealthReporter> logger)
        : this(schedules, recurring, BackgroundJobCatalog.FromJobIds(PlatformJobCatalog.All), logger, TimeProvider.System)
    {
    }

    public BackgroundJobsHealthReporter(
        IJobScheduleProvider schedules,
        IRecurringJobStateReader recurring,
        ILogger<BackgroundJobsHealthReporter> logger,
        TimeProvider timeProvider)
        : this(schedules, recurring, BackgroundJobCatalog.FromJobIds(PlatformJobCatalog.All), logger, timeProvider)
    {
    }

    public async Task<BackgroundJobsHealthReport> GetReportAsync(CancellationToken cancellationToken)
    {
        var generatedAtUtc = timeProvider.GetUtcNowDateTime();

        var rows = new List<BackgroundJobHealthRow>(catalog.All.Count);

        var desiredEnabled = 0;
        var registered = 0;
        var misconfigured = 0;

        foreach (var jobId in catalog.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            JobSchedule? desired = null;
            try
            {
                desired = schedules.GetSchedule(jobId);
            }
            catch (Exception ex)
            {
                // Schedules are supplied by the vertical app; if a provider misbehaves we should surface it.
                logger.LogError(ex, "NGB.BackgroundJobs: schedule provider threw for JobId={JobId}.", jobId);
            }

            var desiredEnabledForJob = desired is { Enabled: true };
            if (desiredEnabledForJob)
                desiredEnabled++;

            var state = await recurring.TryGetAsync(jobId, cancellationToken);
            var isRegistered = state is not null;
            if (isRegistered)
                registered++;

            // Misconfiguration signals drift between desired schedules and actual Hangfire recurring jobs.
            var isMisconfigured = desiredEnabledForJob != isRegistered;
            if (!isMisconfigured && desiredEnabledForJob)
            {
                // If enabled, ensure cron/tz match what we asked Hangfire to configure.
                if (!string.Equals(desired!.Cron, state!.Cron, StringComparison.Ordinal)
                    || !string.Equals(desired.TimeZoneId, state.TimeZoneId, StringComparison.Ordinal))
                {
                    isMisconfigured = true;
                }
            }

            if (isMisconfigured)
                misconfigured++;

            rows.Add(new BackgroundJobHealthRow(
                JobId: jobId,
                DesiredEnabled: desiredEnabledForJob,
                DesiredCron: desired?.Cron,
                DesiredTimeZoneId: desired?.TimeZoneId,
                IsHangfireRegistered: isRegistered,
                HangfireCron: state?.Cron,
                HangfireTimeZoneId: state?.TimeZoneId,
                LastExecutionUtc: state?.LastExecutionUtc,
                NextExecutionUtc: state?.NextExecutionUtc,
                LastJobState: state?.LastJobState,
                Error: state?.Error,
                IsMisconfigured: isMisconfigured));
        }

        return new BackgroundJobsHealthReport(
            GeneratedAtUtc: generatedAtUtc,
            CatalogJobCount: catalog.All.Count,
            DesiredEnabledCount: desiredEnabled,
            HangfireRegisteredCount: registered,
            MisconfiguredCount: misconfigured,
            Jobs: rows);
    }
}
