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

        return new PostgreSqlStorage(
            new NpgsqlConnectionFactory(connectionString, storageOptions, connectionSetup: null!),
            storageOptions);
    }
}
