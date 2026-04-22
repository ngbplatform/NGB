namespace NGB.BackgroundJobs.Contracts;

public enum PlatformJobRunOutcome
{
    Succeeded = 0,
    Failed = 1,
    Cancelled = 2,
    SkippedOverlap = 3,
    SkippedNoImplementation = 4
}
