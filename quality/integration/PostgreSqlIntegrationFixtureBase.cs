using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace NGB.Testing.PostgreSql;

/// <summary>
/// Owns an isolated PostgreSQL container for one xUnit collection.
/// Production migrations run once during fixture startup; ordinary test resets use Respawn only.
/// </summary>
public abstract class PostgreSqlIntegrationFixtureBase : IAsyncLifetime
{
    private static readonly TimeSpan DatabaseReadyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DatabaseReadyRetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly SemaphoreSlim _resetSemaphore = new(1, 1);
    private PostgreSqlContainer? _container;
    private Respawner? _respawner;
    private bool _hasReset;

    protected abstract string DatabaseName { get; }

    protected virtual string PostgreSqlImage => "postgres:16";

    /// <summary>
    /// Schema-mutating tests get a dedicated fixture and rebuild the schema between cases.
    /// Ordinary tests must leave this disabled so resets remain data-only.
    /// </summary>
    protected virtual bool RebuildSchemaBeforeReset => false;

    public string ConnectionString { get; private set; } = string.Empty;

    protected abstract Task ApplyMigrationsAsync(string connectionString, CancellationToken cancellationToken);

    protected virtual Task InitializeAuxiliaryResourcesAsync() => Task.CompletedTask;

    protected virtual ValueTask DisposeAuxiliaryResourcesAsync() => ValueTask.CompletedTask;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder(PostgreSqlImage)
            .WithDatabase(DatabaseName)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await Task.WhenAll(
            _container.StartAsync(),
            InitializeAuxiliaryResourcesAsync());

        ConnectionString = BuildPooledConnectionString(_container.GetConnectionString());

        // Testcontainers waits for PostgreSQL inside the container. On Docker Desktop the
        // host-side port forward may still briefly accept and then close connections, which
        // Npgsql reports as an invalid/empty SSL negotiation response. Verify the actual
        // client path before migrations start instead of relying only on container readiness.
        await WaitUntilDatabaseAcceptsConnectionsAsync(ConnectionString, CancellationToken.None);
        await ApplyMigrationsAsync(ConnectionString, CancellationToken.None);
        _respawner = await CreateRespawnerAsync(CancellationToken.None);
    }

    public async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        await _resetSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (RebuildSchemaBeforeReset && _hasReset)
            {
                await RebuildSchemaAsync(cancellationToken);
            }
            else
            {
                await ResetDataAsync(cancellationToken);
            }

            _hasReset = true;
        }
        finally
        {
            _resetSemaphore.Release();
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            await DisposeAuxiliaryResourcesAsync();
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(ConnectionString))
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                NpgsqlConnection.ClearPool(connection);
            }

            if (_container is not null)
            {
                await _container.DisposeAsync();
            }

            _resetSemaphore.Dispose();
        }
    }

    private static string BuildPooledConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "NGB integration tests",
            Options = "-c timezone=UTC",
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 64,
            ConnectionIdleLifetime = 30,
            ConnectionPruningInterval = 5,
            Timeout = 15,
            CommandTimeout = 30,
            NoResetOnClose = false,
            // The disposable PostgreSQL container is local and is not configured with TLS.
            // Being explicit avoids an unnecessary SSLRequest during every new connection.
            SslMode = SslMode.Disable
        };

        return builder.ConnectionString;
    }

    private static async Task WaitUntilDatabaseAcceptsConnectionsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var readinessConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            Timeout = 3,
            CommandTimeout = 3
        }.ConnectionString;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(DatabaseReadyTimeout);

        Exception? lastError = null;

        while (!timeoutSource.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(readinessConnectionString);
                await connection.OpenAsync(timeoutSource.Token);

                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1;";
                await command.ExecuteScalarAsync(timeoutSource.Token);
                return;
            }
            catch (Exception exception) when (
                IsTransientReadinessFailure(exception) &&
                !cancellationToken.IsCancellationRequested)
            {
                lastError = exception;
                if (timeoutSource.IsCancellationRequested)
                    break;

                try
                {
                    await Task.Delay(DatabaseReadyRetryDelay, timeoutSource.Token);
                }
                catch (OperationCanceledException) when (
                    timeoutSource.IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (
                timeoutSource.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException(
            $"PostgreSQL test container did not accept client connections within {DatabaseReadyTimeout.TotalSeconds:0} seconds.",
            lastError);
    }

    private static bool IsTransientReadinessFailure(Exception exception) =>
        exception switch
        {
            PostgresException postgresException => postgresException.SqlState is
                "08000" or // connection_exception
                "08001" or // sqlclient_unable_to_establish_sqlconnection
                "08003" or // connection_does_not_exist
                "08004" or // sqlserver_rejected_establishment_of_sqlconnection
                "08006" or // connection_failure
                "53300" or // too_many_connections
                "57P03",   // cannot_connect_now
            NpgsqlException => true,
            TimeoutException => true,
            _ => false
        };

    private async Task ResetDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await _respawner!.ResetAsync(connection);
            await RestorePlatformBaselineDataAsync(connection, cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "3F000")
        {
            throw new InvalidOperationException(
                "This test changed the database schema while using a data-only integration-test collection. " +
                "Move it to the project's schema-changing collection so the schema can be rebuilt safely.",
                exception);
        }
    }

    private async Task RebuildSchemaAsync(CancellationToken cancellationToken)
    {
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ApplyMigrationsAsync(ConnectionString, cancellationToken);
        _respawner = await CreateRespawnerAsync(cancellationToken);
    }

    private async Task<Respawner> CreateRespawnerAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        return await Respawner.CreateAsync(
            connection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                // System cash-flow definitions are immutable migration-owned reference data.
                TablesToIgnore = [new Respawn.Graph.Table("public", "accounting_cash_flow_lines")]
            });
    }

    private static async Task RestorePlatformBaselineDataAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        // Respawn truncates mutable tables, including this one reserved invariant row.
        // Reinsert only the invariant instead of rerunning the complete migration/repair pipeline.
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO public.platform_dimension_sets (dimension_set_id)
            VALUES ('00000000-0000-0000-0000-000000000000')
            ON CONFLICT (dimension_set_id) DO NOTHING;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void EnsureInitialized()
    {
        if (_container is null || _respawner is null || string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("The PostgreSQL integration-test fixture has not been initialized.");
        }
    }
}
