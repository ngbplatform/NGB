using Hangfire;
using Medallion.Threading.Postgres;
using NGB.Persistence.BackgroundJobs;
using Npgsql;

namespace NGB.BackgroundJobs.PostgreSql;

public sealed class PostgresBackgroundJobExecutionLockProvider(JobStorage? storage = null)
    : IBackgroundJobExecutionLockProvider
{
    private readonly PostgresHangfireStorageConnections.ConnectionSettings? _storageSettings =
        PostgresHangfireStorageConnections.GetConnectionSettings(storage);

    public async Task<IBackgroundJobExecutionLock?> TryAcquireAsync(
        BackgroundJobExecutionLockRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configuredConnection = _storageSettings?.ConnectionString ?? request.ConnectionString;
        var storageNamespace = _storageSettings?.StorageNamespace ?? request.StorageNamespace;

        ArgumentException.ThrowIfNullOrWhiteSpace(configuredConnection);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Resource);

        PostgresRecurringJobHashBatchReader.ValidateStorageNamespace(storageNamespace);

        ct.ThrowIfCancellationRequested();

        var connectionString = new NpgsqlConnectionStringBuilder(configuredConnection)
        {
            // The lock must outlive any ambient transaction belonging to the job invocation.
            Enlist = false
        }.ConnectionString;

        var key = new PostgresAdvisoryLockKey($"ngb:job-execution:{storageNamespace}:{request.Resource}", allowHashing: true);
        var jobLock = new PostgresDistributedLock(key, connectionString, options => options
            // An internally owned transaction also supports transaction-pooling proxies.
            .UseTransaction()
            .KeepaliveCadence(TimeSpan.FromSeconds(5)));

        var handle = await jobLock.TryAcquireAsync(request.Timeout, ct);

        return handle is null ? null : new ExecutionLock(handle);
    }

    private sealed class ExecutionLock(PostgresDistributedLockHandle handle) : IBackgroundJobExecutionLock
    {
        public CancellationToken LockLostToken => handle.HandleLostToken;

        public ValueTask DisposeAsync() => handle.DisposeAsync();
    }
}
