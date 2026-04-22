namespace NGB.BackgroundJobs.Configuration;

public sealed class JobScheduleOptions
{
    /// <summary>
    /// Cron expression (Hangfire compatible). If null/empty, the job is unscheduled
    /// unless it can default from <see cref="BackgroundJobsSchedulesOptions.NightlyCron"/>.
    /// </summary>
    public string? Cron { get; set; }

    /// <summary>
    /// Per-job switch. When false, the job is unscheduled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional timezone id override for this job.
    /// </summary>
    public string? TimeZoneId { get; set; }
}
