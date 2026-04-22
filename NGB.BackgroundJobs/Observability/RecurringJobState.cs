namespace NGB.BackgroundJobs.Observability;

internal sealed record RecurringJobState(
    string JobId,
    string? Cron,
    string? TimeZoneId,
    DateTime? LastExecutionUtc,
    DateTime? NextExecutionUtc,
    string? LastJobId,
    string? LastJobState,
    string? Error);
