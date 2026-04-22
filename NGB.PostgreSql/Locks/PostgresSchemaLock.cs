using System.Data;
using Dapper;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Locks;

/// <summary>
/// Serializes dynamic DDL for per-entity physical tables.
///
/// Without an advisory lock, concurrent schema ensure operations can race on:
/// - CREATE TRIGGER (no IF NOT EXISTS),
/// - ALTER TABLE / CREATE INDEX, and
/// - pg_trigger existence checks inside DO blocks.
///
/// IMPORTANT:
/// DDL takes heavyweight locks that are held until the end of the current transaction.
/// If we released a session advisory lock before COMMIT, another session could acquire the advisory
/// lock and then block on those heavyweight locks, while the first session later tries to re-acquire
/// the advisory lock (e.g. when ensuring multiple tables in the same transaction).
/// That creates a classic deadlock cycle.
///
/// To prevent this, when an explicit transaction is active we use a transaction-scoped advisory
/// lock (<c>pg_advisory_xact_lock</c>) which is released automatically together with the DDL locks
/// on COMMIT/ROLLBACK. When no explicit transaction is active, we fall back to a session-scoped lock
/// so the caller can still safely run schema ensure outside a transaction.
///
/// The session lock is released in a best-effort <c>finally</c> via <c>pg_advisory_unlock</c>.
/// If unlock fails (e.g., connection already closed), PostgreSQL releases all advisory locks
/// when the session ends.
/// </summary>
internal static class PostgresSchemaLock
{
    public static async Task<IAsyncDisposable> AcquireAsync(
        IUnitOfWork uow,
        int key1,
        Guid entityId,
        int salt,
        CancellationToken ct)
    {
        if (entityId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(entityId), "EntityId must not be empty.");

        await uow.EnsureConnectionOpenAsync(ct);

        var key2 = HashGuidToKey2(entityId, salt);

        // If a transaction is active, use a transaction-scoped lock to avoid deadlocks
        // when DDL locks are held until COMMIT.
        if (uow.HasActiveTransaction)
        {
            var cmd = new CommandDefinition(
                "SELECT pg_advisory_xact_lock(@Key1, @Key2);",
                new { Key1 = key1, Key2 = key2 },
                transaction: uow.Transaction,
                cancellationToken: ct);

            await uow.Connection.ExecuteAsync(cmd);
            return NoOpHandle.Instance;
        }

        // No explicit transaction: use a session lock and best-effort unlock.
        {
            var cmd = new CommandDefinition(
                "SELECT pg_advisory_lock(@Key1, @Key2);",
                new { Key1 = key1, Key2 = key2 },
                transaction: uow.Transaction,
                cancellationToken: ct);

            await uow.Connection.ExecuteAsync(cmd);
            return new SessionLockHandle(uow, key1, key2);
        }
    }

    private sealed class NoOpHandle : IAsyncDisposable
    {
        public static readonly NoOpHandle Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SessionLockHandle(IUnitOfWork uow, int key1, int key2) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                if (uow.Connection.State != ConnectionState.Open)
                    return;

                var cmd = new CommandDefinition(
                    "SELECT pg_advisory_unlock(@Key1, @Key2);",
                    new { Key1 = key1, Key2 = key2 },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None);

                await uow.Connection.ExecuteAsync(cmd);
            }
            catch
            {
                // Best-effort unlock. If it fails, PostgreSQL will release locks on session end.
            }
        }
    }

    private static int HashGuidToKey2(Guid id, int salt)
    {
        // Stable, allocation-free hash of all 16 bytes + salt.
        // Collisions only cause extra serialization, which is fine.
        const uint fnvOffset = 2166136261u;
        const uint fnvPrime = 16777619u;

        Span<byte> bytes = stackalloc byte[16];
        if (!id.TryWriteBytes(bytes))
        {
            throw new NgbInvariantViolationException(
                "Failed to write Guid bytes.",
                new Dictionary<string, object?> { ["id"] = id });
        }

        var h = fnvOffset;

        foreach (var b in bytes)
        {
            h ^= b;
            h *= fnvPrime;
        }

        h ^= unchecked((uint)salt);
        h = Avalanche(h);

        var k = unchecked((int)h);
        return k == 0 ? 1 : k;
    }

    private static uint Avalanche(uint h)
    {
        // Murmur3 finalizer.
        h ^= h >> 16;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        h *= 0xC2B2AE35u;
        h ^= h >> 16;
        return h;
    }
}
