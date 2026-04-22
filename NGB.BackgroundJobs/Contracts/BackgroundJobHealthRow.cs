namespace NGB.BackgroundJobs.Contracts;

public sealed record BackgroundJobHealthRow(
    string JobId,
    // Desired state (from IJobScheduleProvider)
    bool DesiredEnabled,
    string? DesiredCron,
    string? DesiredTimeZoneId,
    // Actual state (from Hangfire storage)
    bool IsHangfireRegistered,
    string? HangfireCron,
    string? HangfireTimeZoneId,
    DateTime? LastExecutionUtc,
    DateTime? NextExecutionUtc,
    string? LastJobState,
    string? Error,
    // Derived
    bool IsMisconfigured);
