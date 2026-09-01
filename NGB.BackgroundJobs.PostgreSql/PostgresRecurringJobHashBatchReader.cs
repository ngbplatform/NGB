using NGB.Persistence.BackgroundJobs;
using NGB.Tools.Exceptions;
using Npgsql;
using NpgsqlTypes;

namespace NGB.BackgroundJobs.PostgreSql;

public sealed class PostgresRecurringJobHashBatchReader : IRecurringJobHashBatchReader
{
    private const string RecurringJobPrefix = "recurring-job:";

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> GetManyAsync(
        RecurringJobHashBatchRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ct.ThrowIfCancellationRequested();

        if (request.JobIds.Count == 0)
            return new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        var schema = QuoteIdentifier(request.StorageNamespace);
        var keys = request.JobIds
            .Distinct(StringComparer.Ordinal)
            .Select(static id => RecurringJobPrefix + id)
            .ToArray();

        await using var connection = new NpgsqlConnection(request.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT key, field, value
                              FROM {schema}."hash"
                              WHERE key = ANY(@Keys);
                              """;
        command.Parameters.AddWithValue("Keys", NpgsqlDbType.Array | NpgsqlDbType.Text, keys);

        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var key = reader.GetString(0);
            if (!key.StartsWith(RecurringJobPrefix, StringComparison.Ordinal))
                continue;

            var jobId = key[RecurringJobPrefix.Length..];
            if (!result.TryGetValue(jobId, out var hash))
            {
                hash = new Dictionary<string, string>(StringComparer.Ordinal);
                result.Add(jobId, hash);
            }

            hash[reader.GetString(1)] = reader.GetString(2);
        }

        return result.ToDictionary(
            static pair => pair.Key,
            static IReadOnlyDictionary<string, string> (pair) => pair.Value,
            StringComparer.Ordinal);
    }

    private static string QuoteIdentifier(string identifier)
    {
        ValidateStorageNamespace(identifier);
        return $"\"{identifier}\"";
    }

    internal static void ValidateStorageNamespace(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)
            || !(char.IsLetter(identifier[0]) || identifier[0] == '_')
            || identifier.Any(static c => !(char.IsLetterOrDigit(c) || c == '_')))
        {
            throw new NgbConfigurationViolationException("Hangfire PostgreSQL storage namespace must be a valid SQL identifier.");
        }
    }
}
