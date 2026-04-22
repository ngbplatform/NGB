using System.Data.Common;
using Microsoft.Extensions.Logging;
using Npgsql;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.UnitOfWork;

public sealed class PostgresUnitOfWork(string connectionString, ILogger<PostgresUnitOfWork> logger)
    : IUnitOfWork
{
    private readonly SemaphoreSlim _openLock = new(1, 1);

    private bool _committedOrRolledBack;
    private bool _sessionInitialized;

    public DbConnection Connection { get; } = new NpgsqlConnection(connectionString);
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

    private async Task InitializeSessionAsync(CancellationToken ct)
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
        logger.LogDebug("DB transaction BEGIN.");

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
        logger.LogDebug("DB transaction COMMIT.");

        if (Transaction is null)
            throw new NgbInvariantViolationException($"No active transaction. Call {nameof(BeginTransactionAsync)}() first.");

        try
        {
            await Transaction.CommitAsync(CancellationToken.None);
            _committedOrRolledBack = true;
        }
        finally
        {
            await Transaction.DisposeAsync();
            Transaction = null;
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        // Transaction finalization MUST NOT depend on the caller's CancellationToken.
        logger.LogWarning("DB transaction ROLLBACK.");

        if (Transaction is null)
            return;

        try
        {
            await Transaction.RollbackAsync(CancellationToken.None);
            _committedOrRolledBack = true;
        }
        finally
        {
            await Transaction.DisposeAsync();
            Transaction = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Fail-safe: if transaction is active and forgot Commit/Rollback — rollback.
        // Dispose MUST NOT depend on CancellationToken either.
        if (Transaction is not null && !_committedOrRolledBack)
        {
            logger.LogWarning("UnitOfWork disposed with active transaction; rolling back.");
            try
            {
                await Transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // ignore: disposing should not throw because of rollback failure
            }
            finally
            {
                await Transaction.DisposeAsync();
                Transaction = null;
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
