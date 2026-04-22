using Npgsql;

namespace NGB.BackgroundJobs.IntegrationTests.Infrastructure;

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

        await using (var terminate = new NpgsqlCommand(
                         "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @dbName;",
                         admin))
        {
            terminate.Parameters.AddWithValue("dbName", DatabaseName);
            await terminate.ExecuteNonQueryAsync();
        }

        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DatabaseName}\";", admin);
        await drop.ExecuteNonQueryAsync();
    }

    private async Task CreateCoreAsync()
    {
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();

        await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DatabaseName}\";", admin))
        {
            await drop.ExecuteNonQueryAsync();
        }

        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{DatabaseName}\";", admin);
        await create.ExecuteNonQueryAsync();
    }

    private static void ValidateDatabaseName(string databaseName)
    {
        if (databaseName.Contains('"'))
            throw new ArgumentException("Database name must not contain quotes.", nameof(databaseName));
    }
}
