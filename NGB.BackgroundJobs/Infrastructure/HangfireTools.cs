using Dapper;
using Npgsql;

namespace NGB.BackgroundJobs.Infrastructure;

public static class HangfireTools
{
    private static string GetDatabaseNameFromConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return builder.Database!;
    }

    public static async Task EnsureDatabaseExistsAsync(string connectionString)
    {
        var databaseName = GetDatabaseNameFromConnectionString(connectionString);

        const string checkDatabaseSql = @"
            SELECT EXISTS (
                SELECT FROM pg_database 
                WHERE datname = quote_ident(@DbName)
            )";

        // Connect to the default database 'postgres'
        var defaultConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres"
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(defaultConnectionString);
        await connection.OpenAsync();

        var exists = await connection.ExecuteScalarAsync<bool>(checkDatabaseSql, new { DbName = databaseName });

        if (!exists)
            await connection.ExecuteAsync($"CREATE DATABASE {databaseName}");
    }
}
