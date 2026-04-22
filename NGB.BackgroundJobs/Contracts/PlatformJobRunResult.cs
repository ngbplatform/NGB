namespace NGB.BackgroundJobs.Contracts;

public sealed record PlatformJobRunResult(
    string JobId,
    string RunId,
    PlatformJobRunOutcome Outcome,
    DateTime StartedUtc,
    DateTime FinishedUtc,
    long DurationMs,
    IReadOnlyDictionary<string, long> Counters,
    Exception? Error)
{
    /// <summary>
    /// Best-effort signal for whether the result indicates a problem worth notifying on.
    ///
    /// Rules:
    /// - any non-succeeded outcome is a problem;
    /// - succeeded runs can still be marked as a problem if the job sets Counters["problem"] != 0.
    /// </summary>
    public bool HasProblems
    {
        get
        {
            if (Outcome != PlatformJobRunOutcome.Succeeded)
                return true;

            if (Counters.TryGetValue("problem", out var p) && p != 0)
                return true;

            if (Counters.TryGetValue("problem_count", out var c) && c > 0)
                return true;

            return false;
        }
    }
}
