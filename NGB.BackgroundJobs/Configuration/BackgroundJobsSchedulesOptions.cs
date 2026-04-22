using NGB.BackgroundJobs.Catalog;

namespace NGB.BackgroundJobs.Configuration;

/// <summary>
/// Configuration model for schedules in the vertical application (appsettings.json).
///
/// Recommended section name: "BackgroundJobs".
/// </summary>
public sealed class BackgroundJobsSchedulesOptions
{
    /// <summary>
    /// Global switch. When false, all recurring jobs are unscheduled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default timezone for all jobs, unless overridden per job.
    /// Prefer "UTC".
    /// </summary>
    public string DefaultTimeZoneId { get; set; } = "UTC";

    /// <summary>
    /// Optional default cron for the nightly pack.
    ///
    /// If provided, it will be used for jobs that do not have an explicit cron.
    /// </summary>
    public string? NightlyCron { get; set; }

    /// <summary>
    /// Jobs to exclude from <see cref="NightlyCron"/> defaulting.
    ///
    /// The default includes the optional frequent stuck monitor.
    /// </summary>
    public string[] NightlyExcludedJobIds { get; set; } =
    [
        PlatformJobCatalog.AccountingOperationsStuckMonitor,
        PlatformJobCatalog.AccountingGeneralJournalEntryAutoReversePostDue
    ];

    /// <summary>
    /// Per-job overrides.
    /// Key is JobId.
    /// </summary>
    public Dictionary<string, JobScheduleOptions> Jobs { get; set; } = new();
}
