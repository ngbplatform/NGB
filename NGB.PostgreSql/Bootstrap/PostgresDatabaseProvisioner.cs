using System.Data.Common;
using NGB.Persistence.Databases;
using NGB.Tools.Exceptions;
using Npgsql;

namespace NGB.PostgreSql.Bootstrap;

/// <summary>
/// Idempotently provisions a PostgreSQL database by connecting to the standard
/// maintenance database. A cross-process advisory lock serializes concurrent NGB
/// creators, while duplicate-database errors cover races with external creators.
/// </summary>
public sealed class PostgresDatabaseProvisioner : IDatabaseProvisioner
{
    private const string DuplicateDatabaseSqlState = "42P04";
    private const string UniqueViolationSqlState = "23505";
    private const string DatabaseNameUniqueConstraint = "pg_database_datname_index";
    private readonly DbProviderFactory _connectionFactory;

    public PostgresDatabaseProvisioner() : this(NpgsqlFactory.Instance)
    {
    }

    internal PostgresDatabaseProvisioner(DbProviderFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task EnsureDatabaseExistsAsync(string connectionString, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new NgbArgumentRequiredException(nameof(connectionString));

        ct.ThrowIfCancellationRequested();

        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = string.IsNullOrWhiteSpace(target.Database)
            ? throw new NgbConfigurationViolationException("PostgreSQL connection string must specify a database.")
            : target.Database;

        target.Database = "postgres";

        await using var connection = _connectionFactory.CreateConnection()
            ?? throw new NgbConfigurationViolationException("The PostgreSQL provider did not create a connection.");

        connection.ConnectionString = target.ConnectionString;

        await connection.OpenAsync(ct);
        await AcquireProvisioningLockAsync(connection, databaseName, ct);

        try
        {
            if (await DatabaseExistsAsync(connection, databaseName, ct))
                return;

            var quotedDatabaseName = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
            await using var create = connection.CreateCommand();
            create.CommandText = $"CREATE DATABASE {quotedDatabaseName}";

            try
            {
                await create.ExecuteNonQueryAsync(ct);
            }
            catch (PostgresException ex) when (ex.SqlState == DuplicateDatabaseSqlState
                || ex is { SqlState: UniqueViolationSqlState, ConstraintName: DatabaseNameUniqueConstraint })
            {
                // A non-NGB database creator won the race after our existence check.
                if (!await DatabaseExistsAsync(connection, databaseName, ct))
                    throw;
            }
        }
        finally
        {
            // Session locks survive while a pooled physical connection remains open.
            // Always unlock explicitly before returning the connection to the pool.
            await ReleaseProvisioningLockAsync(connection, databaseName);
        }
    }

    private static async Task AcquireProvisioningLockAsync(
        DbConnection connection,
        string databaseName,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_lock(hashtextextended(@LockName, 0));";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "LockName";
        parameter.Value = $"ngb:database-provision:{databaseName}";
        command.Parameters.Add(parameter);

        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task ReleaseProvisioningLockAsync(
        DbConnection connection,
        string databaseName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@LockName, 0));";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "LockName";
        parameter.Value = $"ngb:database-provision:{databaseName}";
        command.Parameters.Add(parameter);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<bool> DatabaseExistsAsync(
        DbConnection connection,
        string databaseName,
        CancellationToken ct)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @DatabaseName);";

        var parameter = check.CreateParameter();
        parameter.ParameterName = "DatabaseName";
        parameter.Value = databaseName;
        check.Parameters.Add(parameter);

        return await check.ExecuteScalarAsync(ct) is true;
    }
}
