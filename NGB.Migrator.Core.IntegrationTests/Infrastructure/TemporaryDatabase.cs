using Dapper;
using Npgsql;

namespace NGB.Migrator.Core.IntegrationTests.Infrastructure;

internal sealed class TemporaryDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private bool _disposed;

    private TemporaryDatabase(string adminConnectionString, string databaseName, string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        DatabaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string DatabaseName { get; }
    public string ConnectionString { get; }

    public static async Task<TemporaryDatabase> CreateAsync(string baseConnectionString, string databaseNamePrefix)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[..16];
        var databaseName = $"{databaseNamePrefix}_{suffix}";

        ValidateDatabaseName(databaseName);

        var csb = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };

        var adminCsb = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };

        var db = new TemporaryDatabase(adminCsb.ConnectionString, databaseName, csb.ConnectionString);
        await db.CreateCoreAsync();
        return db;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();

        await admin.ExecuteAsync(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @DbName;",
            new { DbName = DatabaseName });

        await admin.ExecuteAsync($"DROP DATABASE IF EXISTS \"{DatabaseName}\";");
    }

    private async Task CreateCoreAsync()
    {
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();

        await admin.ExecuteAsync($"DROP DATABASE IF EXISTS \"{DatabaseName}\";");
        await admin.ExecuteAsync($"CREATE DATABASE \"{DatabaseName}\";");
    }

    private static void ValidateDatabaseName(string databaseName)
    {
        if (databaseName.Contains('"'))
            throw new ArgumentException("Database name must not contain quotes.", nameof(databaseName));
    }
}
