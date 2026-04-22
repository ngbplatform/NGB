namespace NGB.BackgroundJobs.Contracts;

/// <summary>
/// Provides schedules for jobs (typically via appsettings.json in the vertical app).
/// Returning <c>null</c> means "do not schedule".
/// </summary>
public interface IJobScheduleProvider
{
    JobSchedule? GetSchedule(string jobId);
}
