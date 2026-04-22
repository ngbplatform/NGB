namespace NGB.BackgroundJobs.Contracts;

/// <summary>
/// Schedule definition for a background job.
///
/// Cron format is Hangfire-compatible. Prefer UTC.
/// </summary>
public sealed record JobSchedule(
    string JobId,
    string Cron,
    bool Enabled = true,
    string TimeZoneId = "UTC");
