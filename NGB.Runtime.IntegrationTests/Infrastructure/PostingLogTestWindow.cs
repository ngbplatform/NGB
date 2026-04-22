namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// Integration tests read posting_log by an explicit time window.
///
/// posting_log timestamps (StartedAtUtc/CompletedAtUtc) are operation times ("wall clock"),
/// not accounting period times, so tests must not rely on tight DateTime.UtcNow ± small deltas.
///
/// This helper captures a wide, deterministic window for the duration of a test.
/// </summary>
internal readonly record struct PostingLogTestWindow(DateTime FromUtc, DateTime ToUtc)
{
    public static PostingLogTestWindow Capture(
        TimeSpan? lookBack = null,
        TimeSpan? lookAhead = null)
    {
        var now = DateTime.UtcNow;
        return new PostingLogTestWindow(
            FromUtc: now - (lookBack ?? TimeSpan.FromHours(1)),
            ToUtc: now + (lookAhead ?? TimeSpan.FromHours(1)));
    }
}
