namespace NGB.BackgroundJobs.Contracts;

public sealed record BackgroundJobsHealthReport(
    DateTime GeneratedAtUtc,
    int CatalogJobCount,
    int DesiredEnabledCount,
    int HangfireRegisteredCount,
    int MisconfiguredCount,
    IReadOnlyList<BackgroundJobHealthRow> Jobs);
