using System.Globalization;
using Hangfire;
using Microsoft.Extensions.Options;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.Persistence.BackgroundJobs;

namespace NGB.BackgroundJobs.Observability;

internal sealed class HangfireRecurringJobStateReader : IRecurringJobStateReader
{
    private readonly JobStorage _jobStorage;
    private readonly PlatformHangfireOptions? _options;
    private readonly IRecurringJobHashBatchReader? _batchReader;

    internal HangfireRecurringJobStateReader(JobStorage jobStorage)
    {
        _jobStorage = jobStorage;
    }

    internal HangfireRecurringJobStateReader(
        JobStorage jobStorage,
        IOptions<PlatformHangfireOptions> options,
        IRecurringJobHashBatchReader? batchReader)
    {
        _jobStorage = jobStorage;
        _options = options.Value;
        _batchReader = batchReader;
    }

    public async ValueTask<IReadOnlyDictionary<string, RecurringJobState>> GetManyAsync(
        IReadOnlyCollection<string> jobIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var distinctJobIds = jobIds.Distinct(StringComparer.Ordinal).ToArray();
        if (_batchReader is not null && _options is not null)
        {
            var hashes = await _batchReader.GetManyAsync(
                new RecurringJobHashBatchRequest(
                    _options.ConnectionString,
                    _options.StorageNamespace,
                    distinctJobIds),
                cancellationToken);
            return BuildStates(distinctJobIds, hashes, cancellationToken);
        }

        // Compatibility fallback for non-PostgreSQL/custom Hangfire storage.
        var states = new Dictionary<string, RecurringJobState>(StringComparer.Ordinal);
        using var connection = _jobStorage.GetConnection();
        foreach (var jobId in distinctJobIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = $"recurring-job:{jobId}";
            var hash = connection.GetAllEntriesFromHash(key);
            if (hash is null || hash.Count == 0)
                continue;

            states[jobId] = BuildState(jobId, hash);
        }

        return states;
    }

    private static IReadOnlyDictionary<string, RecurringJobState> BuildStates(
        IReadOnlyList<string> jobIds,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> hashes,
        CancellationToken ct)
    {
        var states = new Dictionary<string, RecurringJobState>(StringComparer.Ordinal);
        foreach (var jobId in jobIds)
        {
            ct.ThrowIfCancellationRequested();
            if (hashes.TryGetValue(jobId, out var hash) && hash.Count > 0)
                states[jobId] = BuildState(jobId, hash);
        }

        return states;
    }

    private static RecurringJobState BuildState(string jobId, IReadOnlyDictionary<string, string> hash)
    {
        hash.TryGetValue("Cron", out var cron);
        hash.TryGetValue("TimeZoneId", out var tz);
        hash.TryGetValue("LastExecution", out var lastExec);
        hash.TryGetValue("NextExecution", out var nextExec);
        hash.TryGetValue("LastJobId", out var lastJobId);
        hash.TryGetValue("LastJobState", out var lastState);
        hash.TryGetValue("Error", out var error);

        return new RecurringJobState(
            jobId,
            cron,
            tz,
            ParseUtc(lastExec),
            ParseUtc(nextExec),
            lastJobId,
            lastState,
            error);
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
