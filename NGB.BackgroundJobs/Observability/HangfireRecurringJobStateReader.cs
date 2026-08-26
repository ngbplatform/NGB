using System.Globalization;
using Hangfire;

namespace NGB.BackgroundJobs.Observability;

internal sealed class HangfireRecurringJobStateReader(JobStorage jobStorage) : IRecurringJobStateReader
{
    public ValueTask<IReadOnlyDictionary<string, RecurringJobState>> GetManyAsync(
        IReadOnlyCollection<string> jobIds,
        CancellationToken cancellationToken)
    {
        // Hangfire storage connection APIs are synchronous.
        cancellationToken.ThrowIfCancellationRequested();

        var states = new Dictionary<string, RecurringJobState>(StringComparer.Ordinal);
        using var connection = jobStorage.GetConnection();
        foreach (var jobId in jobIds.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = $"recurring-job:{jobId}";
            var hash = connection.GetAllEntriesFromHash(key);
            if (hash is null || hash.Count == 0)
                continue;

            hash.TryGetValue("Cron", out var cron);
            hash.TryGetValue("TimeZoneId", out var tz);
            hash.TryGetValue("LastExecution", out var lastExec);
            hash.TryGetValue("NextExecution", out var nextExec);
            hash.TryGetValue("LastJobId", out var lastJobId);
            hash.TryGetValue("LastJobState", out var lastState);
            hash.TryGetValue("Error", out var error);

            states[jobId] = new RecurringJobState(
                jobId,
                cron,
                tz,
                ParseUtc(lastExec),
                ParseUtc(nextExec),
                lastJobId,
                lastState,
                error);
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, RecurringJobState>>(states);
    }

    private static DateTime? ParseUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // Hangfire uses roundtrip "o" format.
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();

        return null;
    }
}
