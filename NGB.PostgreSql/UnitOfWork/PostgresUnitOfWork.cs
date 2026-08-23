using System.Data.Common;
using Microsoft.Extensions.Logging;
using Npgsql;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.UnitOfWork;

public sealed class PostgresUnitOfWork : IUnitOfWork
{
    private readonly SemaphoreSlim _openLock = new(1, 1);
    private readonly ILogger<PostgresUnitOfWork> _logger;

    private bool _committedOrRolledBack;
    private bool _sessionInitialized;

    public PostgresUnitOfWork(string connectionString, ILogger<PostgresUnitOfWork> logger)
        : this(new NpgsqlConnection(connectionString), logger)
    {
    }

    internal PostgresUnitOfWork(DbConnection connection, ILogger<PostgresUnitOfWork> logger)
    {
        Connection = connection ?? throw new NgbArgumentRequiredException(nameof(connection));
        _logger = logger ?? throw new NgbArgumentRequiredException(nameof(logger));
    }

    public DbConnection Connection { get; }
    public DbTransaction? Transaction { get; private set; }
    public bool HasActiveTransaction => Transaction is not null;

    public async Task EnsureConnectionOpenAsync(CancellationToken ct = default)
    {
        if (Connection.State == System.Data.ConnectionState.Open)
            return;

        await _openLock.WaitAsync(ct);
        try
        {
            if (Connection.State == System.Data.ConnectionState.Open)
                return;

            await Connection.OpenAsync(ct);

            // Defense-in-depth:
            // Most of the schema uses TIMESTAMPTZ and expects UTC semantics.
            // We enforce the session timezone explicitly to eliminate any dependency on
            // server defaults, connection pool state, or caller-provided connection strings.
            await InitializeSessionAsync(ct);
        }
        finally
        {
            _openLock.Release();
        }
    }

    internal async Task InitializeSessionAsync(CancellationToken ct)
    {
        if (_sessionInitialized)
            return;

        // If the underlying connection is not Npgsql, do nothing.
        if (Connection is not NpgsqlConnection npgsql)
        {
            _sessionInitialized = true;
            return;
        }

        await using var cmd = npgsql.CreateCommand();
        cmd.CommandText = "SET TIME ZONE 'UTC';";
        await cmd.ExecuteNonQueryAsync(ct);

        _sessionInitialized = true;
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("DB transaction BEGIN.");

        if (Transaction is not null)
            return;

        await EnsureConnectionOpenAsync(ct);
        Transaction = await Connection.BeginTransactionAsync(ct);
        _committedOrRolledBack = false;
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        // Transaction finalization MUST NOT depend on the caller's CancellationToken.
        // If ct is already canceled, Commit/Rollback still must complete to avoid poisoning the process
        // with an open transaction and held advisory locks.
        _logger.LogDebug("DB transaction COMMIT.");

        var transaction = Transaction;
        if (transaction is null)
            throw new NgbInvariantViolationException($"No active transaction. Call {nameof(BeginTransactionAsync)}() first.");

        try
        {
            await transaction.CommitAsync(CancellationToken.None);
            _committedOrRolledBack = true;
        }
        finally
        {
            Transaction = null;
            await transaction.DisposeAsync();
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        // Transaction finalization MUST NOT depend on the caller's CancellationToken.
        _logger.LogWarning("DB transaction ROLLBACK.");

        var transaction = Transaction;
        if (transaction is null)
            return;

        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _committedOrRolledBack = true;
        }
        finally
        {
            Transaction = null;
            await transaction.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Fail-safe: if transaction is active and forgot Commit/Rollback — rollback.
        // Dispose MUST NOT depend on CancellationToken either.
        var transaction = Transaction;
        if (transaction is not null && !_committedOrRolledBack)
        {
            _logger.LogWarning("UnitOfWork disposed with active transaction; rolling back.");
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // ignore: disposing should not throw because of rollback failure
            }
            finally
            {
                Transaction = null;
                await transaction.DisposeAsync();
            }
        }

        await Connection.DisposeAsync();
    }

    public void EnsureActiveTransaction()
    {
        if (!HasActiveTransaction || Transaction is null)
            throw new NgbInvariantViolationException("This operation requires an active transaction.");
    }
}
