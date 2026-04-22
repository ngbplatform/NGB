using System.Globalization;
using Hangfire;

namespace NGB.BackgroundJobs.Observability;

internal sealed class HangfireRecurringJobStateReader(JobStorage jobStorage) : IRecurringJobStateReader
{
    public ValueTask<RecurringJobState?> TryGetAsync(string jobId, CancellationToken cancellationToken)
    {
        // Hangfire storage connection APIs are synchronous.
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = jobStorage.GetConnection();
        var key = $"recurring-job:{jobId}";
        var hash = connection.GetAllEntriesFromHash(key);

        if (hash is null || hash.Count == 0)
            return ValueTask.FromResult<RecurringJobState?>(null);

        hash.TryGetValue("Cron", out var cron);
        hash.TryGetValue("TimeZoneId", out var tz);
        hash.TryGetValue("LastExecution", out var lastExec);
        hash.TryGetValue("NextExecution", out var nextExec);
        hash.TryGetValue("LastJobId", out var lastJobId);
        hash.TryGetValue("LastJobState", out var lastState);
        hash.TryGetValue("Error", out var error);

        return ValueTask.FromResult<RecurringJobState?>(new RecurringJobState(
            jobId,
            cron,
            tz,
            ParseUtc(lastExec),
            ParseUtc(nextExec),
            lastJobId,
            lastState,
            error));
    }

    private static DateTime? ParseUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // Hangfire uses roundtrip "o" format.
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        }

        return null;
    }
}
