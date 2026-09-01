using System.Globalization;
using Dapper;
using Hangfire;
using Microsoft.Extensions.Options;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.Tools.Exceptions;
using Npgsql;

namespace NGB.BackgroundJobs.Observability;

internal sealed class HangfireRecurringJobStateReader : IRecurringJobStateReader
{
    private readonly JobStorage _jobStorage;
    private readonly IRecurringJobHashBatchReader? _batchReader;

    internal HangfireRecurringJobStateReader(JobStorage jobStorage, IRecurringJobHashBatchReader? batchReader = null)
    {
        _jobStorage = jobStorage;
        _batchReader = batchReader;
    }

    public async ValueTask<IReadOnlyDictionary<string, RecurringJobState>> GetManyAsync(
        IReadOnlyCollection<string> jobIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var distinctJobIds = jobIds.Distinct(StringComparer.Ordinal).ToArray();
        if (_batchReader is not null)
        {
            var hashes = await _batchReader.GetManyAsync(distinctJobIds, cancellationToken);
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

internal interface IRecurringJobHashBatchReader
{
    Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> GetManyAsync(
        IReadOnlyCollection<string> jobIds,
        CancellationToken ct);
}

internal sealed class PostgresRecurringJobHashBatchReader(IOptions<PlatformHangfireOptions> options)
    : IRecurringJobHashBatchReader
{
    private const string RecurringJobPrefix = "recurring-job:";

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> GetManyAsync(
        IReadOnlyCollection<string> jobIds,
        CancellationToken ct)
    {
        if (jobIds.Count == 0)
            return new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        var settings = options.Value;
        var schema = QuoteIdentifier(settings.SchemaName);
        var keys = jobIds.Select(static id => RecurringJobPrefix + id).ToArray();
        var sql = $"""
                   SELECT key AS "Key", field AS "Field", value AS "Value"
                   FROM {schema}."hash"
                   WHERE key = ANY(@Keys);
                   """;

        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        var rows = await connection.QueryAsync<HashRow>(new CommandDefinition(
            sql,
            new { Keys = keys },
            cancellationToken: ct));

        return rows
            .Where(static row => row.Key.StartsWith(RecurringJobPrefix, StringComparison.Ordinal))
            .GroupBy(static row => row.Key[RecurringJobPrefix.Length..], StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyDictionary<string, string>)group.ToDictionary(
                    static row => row.Field,
                    static row => row.Value,
                    StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private static string QuoteIdentifier(string identifier)
    {
        ValidateSchemaName(identifier);
        return $"\"{identifier}\"";
    }

    internal static void ValidateSchemaName(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)
            || !(char.IsLetter(identifier[0]) || identifier[0] == '_')
            || identifier.Any(static c => !(char.IsLetterOrDigit(c) || c == '_')))
        {
            throw new NgbConfigurationViolationException("Hangfire PostgreSQL SchemaName must be a valid SQL identifier.");
        }
    }

    private sealed record HashRow(string Key, string Field, string Value);
}
