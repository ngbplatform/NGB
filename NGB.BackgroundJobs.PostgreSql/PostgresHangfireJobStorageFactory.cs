using System.Runtime.CompilerServices;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;

namespace NGB.BackgroundJobs.PostgreSql;

/// <summary>
/// PostgreSQL adapter for constructing Hangfire storage. Application composition
/// roots pass the resulting provider-neutral <see cref="JobStorage"/> to the
/// BackgroundJobs layer.
/// </summary>
public static class PostgresHangfireJobStorageFactory
{
    private static readonly ConditionalWeakTable<JobStorage, ConnectionSettings> StorageConnections = new();

    public static JobStorage Create(string connectionString, string storageNamespace, bool prepareSchemaIfNecessary)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A Hangfire PostgreSQL connection string is required.", nameof(connectionString));

        PostgresRecurringJobHashBatchReader.ValidateStorageNamespace(storageNamespace);

        var storageOptions = new PostgreSqlStorageOptions
        {
            PrepareSchemaIfNecessary = prepareSchemaIfNecessary,
            SchemaName = storageNamespace
        };

        var storage = new PostgreSqlStorage(
            new NpgsqlConnectionFactory(connectionString, storageOptions, connectionSetup: null!),
            storageOptions);

        // Retain the actual storage configuration without wrapping JobStorage or requiring duplicate options.
        StorageConnections.Add(storage, new ConnectionSettings(connectionString, storageNamespace));

        return storage;
    }

    internal static ConnectionSettings? GetConnectionSettings(JobStorage? storage)
    {
        return storage is not null && StorageConnections.TryGetValue(storage, out var settings) ? settings : null;
    }

    internal sealed record ConnectionSettings(string ConnectionString, string StorageNamespace);
}
