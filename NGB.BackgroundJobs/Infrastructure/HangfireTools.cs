using Dapper;
using Npgsql;
using System.Data.Common;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Infrastructure;

public static class HangfireTools
{
    private static string GetDatabaseNameFromConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return string.IsNullOrWhiteSpace(builder.Database)
            ? throw new NgbConfigurationViolationException("Hangfire connection string must specify a database.")
            : builder.Database;
    }

    public static async Task EnsureDatabaseExistsAsync(string connectionString)
        => await EnsureDatabaseExistsAsync(connectionString, NpgsqlFactory.Instance);

    internal static async Task EnsureDatabaseExistsAsync(string connectionString, DbProviderFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        var databaseName = GetDatabaseNameFromConnectionString(connectionString);

        const string checkDatabaseSql = @"
            SELECT EXISTS (
                SELECT FROM pg_database 
                WHERE datname = @DbName
            )";

        // Connect to the default database 'postgres'
        var defaultConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres"
        }.ConnectionString;

        await using var connection = connectionFactory.CreateConnection()
            ?? throw new NgbConfigurationViolationException("The PostgreSQL provider did not create a connection.");
        connection.ConnectionString = defaultConnectionString;
        await connection.OpenAsync();

        var exists = await connection.ExecuteScalarAsync<bool>(checkDatabaseSql, new { DbName = databaseName });

        if (!exists)
        {
            var quotedDatabaseName = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
            await connection.ExecuteAsync($"CREATE DATABASE {quotedDatabaseName}");
        }
    }
}
