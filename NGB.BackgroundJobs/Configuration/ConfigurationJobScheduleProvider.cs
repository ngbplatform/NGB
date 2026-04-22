using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.BackgroundJobs.Contracts;

namespace NGB.BackgroundJobs.Configuration;

/// <summary>
/// Default schedule provider that reads schedules from <see cref="BackgroundJobsSchedulesOptions"/>
/// bound from configuration (appsettings.json) in the vertical application.
/// </summary>
public sealed class ConfigurationJobScheduleProvider(
    IOptions<BackgroundJobsSchedulesOptions> options,
    ILogger<ConfigurationJobScheduleProvider> logger)
    : IJobScheduleProvider
{
    public JobSchedule? GetSchedule(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return null;

        var o = options.Value;
        if (!o.Enabled)
            return null;

        if (o.Jobs.TryGetValue(jobId, out var perJob))
        {
            if (!perJob.Enabled)
                return null;

            var cron = NormalizeCron(perJob.Cron);
            if (string.IsNullOrWhiteSpace(cron))
            {
                cron = TryGetNightlyDefault(jobId, o);
                if (cron is null)
                    return null;
            }

            var tz = string.IsNullOrWhiteSpace(perJob.TimeZoneId)
                ? o.DefaultTimeZoneId
                : perJob.TimeZoneId!;

            return new JobSchedule(jobId, cron, Enabled: true, TimeZoneId: tz);
        }

        // Not explicitly configured: allow a single nightly default.
        var nightly = TryGetNightlyDefault(jobId, o);
        if (nightly is null)
            return null;

        return new JobSchedule(jobId, nightly, Enabled: true, TimeZoneId: o.DefaultTimeZoneId);
    }

    private string? TryGetNightlyDefault(string jobId, BackgroundJobsSchedulesOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.NightlyCron))
            return null;

        if (o.NightlyExcludedJobIds.Any(x => string.Equals(x, jobId, StringComparison.OrdinalIgnoreCase)))
            return null;

        var cron = NormalizeCron(o.NightlyCron);
        if (string.IsNullOrWhiteSpace(cron))
        {
            logger.LogWarning("NGB.BackgroundJobs: NightlyCron is configured but empty after normalization.");
            return null;
        }

        return cron;
    }

    private static string? NormalizeCron(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
            return null;

        return cron.Trim();
    }
}
